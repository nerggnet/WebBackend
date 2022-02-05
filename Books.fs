namespace WebBackend

open System
open System.IO

open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Extensions.Http

open Microsoft.Extensions.Logging

open FSharp.Json

open AzureStorageController

module Books =
    type CommandJson =
        {
            Action: string option
        }

    type BookJson =
        {
            Author: string option
            Title: string option
            IsFavorite: bool option
        }

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

    let getBookFromReqBody (body: string) (log: ILogger) : BookJson =
        try
            let book = Json.deserialize<BookJson> body
            book
        with
            ex ->
                log.LogWarning <| "Get Book failed with exception:\n" + ex.ToString()
                { Author = None; Title = None; IsFavorite = None }

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
                    | (None, None) ->
                        let error = "Cannot execute 'Find' without either 'Author' or 'Title'."
                        log.LogWarning error
                        { Books = []; Error = Some error }
                    | (Some author, None) ->
                        let books = findBooksUsingAuthor tableClient author log
                        match books with
                        | [] ->
                            let error = "Could not find any books written by Author: '" + author + "'."
                            log.LogWarning error
                            { Books = []; Error = Some error }
                        | books -> { Books = books; Error = None }
                    | (None, Some title) ->
                        let books = findBooksUsingTitle tableClient title log
                        match books with
                        | [] ->
                            let error = "Could not find any books with Title: '" + title + "'."
                            log.LogWarning error
                            { Books = []; Error = Some error }
                        | books -> { Books = books; Error = None }
                    | (Some author, Some title) ->
                        let book = findBookUsingAuthorAndTitle tableClient author title log
                        match book with
                        | None ->
                            let error = "Could not find any books with Title: '" + title + "'."
                            log.LogWarning error
                            { Books = []; Error = Some error }
                        | Some book -> { Books = [book]; Error = None }
                | Some "Insert" ->
                    match (bookRes.Author, bookRes.Title, bookRes.IsFavorite) with
                    | (None, None, _) | (None, _, _) | (_, None, _) ->
                        let error = "Could not Insert book without both 'Author' and 'Title'."
                        log.LogWarning error
                        { Books = []; Error = Some error }
                    | (author, title, isFavorite) ->
                        let book =
                            match isFavorite with
                            | Some isFavorite -> { Author = author.Value; Title = title.Value; IsFavorite = isFavorite } : Book
                            | None -> { Author = author.Value; Title = title.Value; IsFavorite = false } : Book
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
                | { Books = _; Error = Some error } ->
                    BadRequestObjectResult error
                | { Books = books; Error = None } ->
                    OkObjectResult books
            return result
        }