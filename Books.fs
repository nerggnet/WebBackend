module WebBackend.Books

open System
open System.IO

open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Extensions.Http

open Microsoft.Extensions.Logging

open FSharp.Json

open Domain
open AzureStorageController

type CommandJson =
    {
        Action: string option
    }

// type BookJson =
//     {
//         Author: string option
//         Title: string option
//         IsFavorite: bool option
//     }

type ResponseJson =
    {
        Books: Book list
        Error: string option
    }

let getCommandFromReqBody (body: string) (log: ILogger) : CommandJson =
    try
        let command = Json.deserialize<CommandJson> body
        command
    with
        ex ->
            log.LogWarning <| "Get Command failed with exception:\n" + ex.ToString()
            { Action = None }

let getBookFromReqBody (body: string) (log: ILogger) : Book =
    try
        let book = Json.deserialize<Book> body
        book
    with
        ex ->
            log.LogWarning <| "Get Book failed with exception:\n" + ex.ToString()
            { Author = ""; Title = ""; IsFavorite = false }

[<FunctionName("Books")>]
let run ([<HttpTrigger(AuthorizationLevel.Function, "post", Route = null)>]req: HttpRequest) (log: ILogger) =
    task {
        log.LogInformation("WebBackend.Books received a POST request")

        use stream = new StreamReader(req.Body)
        let! reqBody = stream.ReadToEndAsync() |> Async.AwaitTask

        let commandRes = getCommandFromReqBody reqBody log
        let bookRes = getBookFromReqBody reqBody log

        let connectionString = findStorageConnectionString log
        let tableClient = initTableClient connectionString

        let response =
            match commandRes.Action with
            | Some "Find" ->
                match (bookRes.Author, bookRes.Title) with
                | ("", "") ->
                    let message = "No 'Author' or 'Title' specified, trying to return all books."
                    log.LogInformation message
                    let books = getBooksFromTable tableClient
                    match books with
                    | [] ->
                        let error = "Could not find any books at all."
                        log.LogWarning error
                        { Books = []; Error = Some error }
                    | books -> { Books = books; Error = None }
                | (author, "") ->
                    let books = findBooksUsingAuthor tableClient author log
                    match books with
                    | [] ->
                        let error = "Could not find any books written by Author: '" + author + "'."
                        log.LogWarning error
                        { Books = []; Error = Some error }
                    | books -> { Books = books; Error = None }
                | ("", title) ->
                    let books = findBooksUsingTitle tableClient title log
                    match books with
                    | [] ->
                        let error = "Could not find any books with Title: '" + title + "'."
                        log.LogWarning error
                        { Books = []; Error = Some error }
                    | books -> { Books = books; Error = None }
                | (author, title) ->
                    let book = findBookUsingAuthorAndTitle tableClient author title log
                    match book with
                    | None ->
                        let error = "Could not find any books with Title: '" + title + "'."
                        log.LogWarning error
                        { Books = []; Error = Some error }
                    | Some book -> { Books = [book]; Error = None }
            | Some "Insert" ->
                match (bookRes.Author, bookRes.Title, bookRes.IsFavorite) with
                | ("", "", _) | ("", _, _) | (_, "", _) ->
                    let error = "Could not Insert book without both 'Author' and 'Title'."
                    log.LogWarning error
                    { Books = []; Error = Some error }
                | (author, title, isFavorite) ->
                    let book = { Author = author; Title = title; IsFavorite = isFavorite } : Book
                    insertBookInTable tableClient book log
                    { Books = [book]; Error = None }
            | Some action ->
                let error = "Unknown Action: '" + action + "'."
                log.LogWarning error
                { Books = []; Error = Some error }
            | None ->
                let error = "No Action specified."
                log.LogWarning error
                { Books = []; Error = Some error }

        let result : IActionResult =
            match response with
            | { Books = books; Error = Some error } ->
                let logMessage = sprintf "Something went wrong, these were the recipes: '%A', and this was the error message: '%s'" books error
                log.LogWarning logMessage
                BadRequestObjectResult error
            | { Books = books; Error = None } ->
                let serializedBooks = Json.serialize books
                let logMessage = sprintf "It looks like everything went well, these are the serialized books: '%A'" serializedBooks
                log.LogInformation logMessage
                OkObjectResult serializedBooks
        return result
    }