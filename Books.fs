namespace WebBackend

open System
open System.IO

open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Extensions.Http
open Azure.Identity
open Azure.Security.KeyVault.Secrets
//open Microsoft.Azure.KeyVault
//open Microsoft.Azure.Services.AppAuthentication
open Microsoft.Azure.Cosmos.Table

//open Microsoft.Extensions.Configuration.AzureKeyVault
open Microsoft.Extensions.Logging

open FSharp.Azure.Storage.Table
open FSharp.Json

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

    type Book =
        {
            [<PartitionKey>] Author: string
            [<RowKey>] Title: string
            IsFavorite: bool
        }

    type ResponseJson =
        {
            Books: Book list
            Error: string option
        }

    let initTableClient (connectionString: string) : CloudTableClient =
        let actualConnectionString =
            match connectionString with
            | "" -> "UseDevelopmentStorage=true;"
            | s -> s
        let account = CloudStorageAccount.Parse actualConnectionString
        let tableClient = account.CreateCloudTableClient()
        tableClient
 
    let getBooksFromTable (tableClient: CloudTableClient) : Book list =
        let fromBookTable q = fromTable tableClient "Books" q
        let books =
            Query.all<Book>
            |> fromBookTable
            |> Seq.map (fun (b,_) -> b)
            |> Seq.toList
        books

    let findBookUsingAuthorAndTitle (tableClient: CloudTableClient) (author: string) (title: string) (log: ILogger) : Book option =
        let fromBookTable q = fromTable tableClient "Books" q
        log.LogInformation <| "Trying to find book with Author: '" + author + "' and Title: '" + title + "'."
        let books =
            Query.all<Book>
            |> Query.where <@ fun _ s -> s.PartitionKey = author && s.RowKey = title @>
            |> fromBookTable
            |> Seq.map (fun (b,_) -> b)
            |> Seq.toList
        match books with
        | [] -> None
        | (book::_) -> Some book

    let findBooksUsingAuthor (tableClient: CloudTableClient) (author: string) (log: ILogger) : Book list =
        let fromBookTable q = fromTable tableClient "Books" q
        log.LogInformation <| "Trying to find books with Author: '" + author + "'."
        let books =
            Query.all<Book>
            |> Query.where <@ fun _ s -> s.PartitionKey = author @>
            |> fromBookTable
            |> Seq.map (fun (b,_) -> b)
            |> Seq.toList
        books

    let findBooksUsingTitle (tableClient: CloudTableClient) (title: string) (log: ILogger) : Book list =
        let fromBookTable q = fromTable tableClient "Books" q
        log.LogInformation <| "Trying to find books with Title: '" + title + "'."
        let books =
            Query.all<Book>
            |> Query.where <@ fun _ s -> s.RowKey = title @>
            |> fromBookTable
            |> Seq.map (fun (b,_) -> b)
            |> Seq.toList
        books

    let findStorageConnectionString (log: ILogger) : string =
        log.LogInformation <| "Trying to find Azure Storage Connection String"
        let connectionStringCandidate = Environment.GetEnvironmentVariable "StorageConnectionString"
        log.LogInformation <| "Found this connection string: '" + connectionStringCandidate + "'."
        let connectionString = if String.IsNullOrWhiteSpace(connectionStringCandidate) then "" else connectionStringCandidate
        connectionString

    let insertBookInTable (tableClient: CloudTableClient) (book: Book) (log: ILogger) : unit =
        let inBookTable book = inTable tableClient "Books" book
        try
            let result = book |> Insert |> inBookTable
            ignore <| match result.HttpStatusCode with
                      | 200 | 201 | 202 | 203 | 204 | 205 -> log.LogInformation <| "Book '" + book.ToString() + "' successfully inserted."
                      | code -> log.LogWarning <| "Could not insert book '" + book.ToString() + "'.\nHTTP Status: '" + code.ToString() + "'."
        with
            | :? StorageException as sx ->
                match sx.Message with
                | "Conflict" -> log.LogWarning <| "Insert failed due to conflicting Keys, PartitionKey: '" + book.Author + "', RowKey: '" + book.Title + "'."
                | _ -> log.LogWarning <| "Insert failed with exception:\n" + sx.ToString()
            | ex -> log.LogWarning <| "Insert failed with exception:\n" + ex.ToString()

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
                            | Some isFavorite -> { Author = author.Value; Title = title.Value; IsFavorite = isFavorite }
                            | None -> { Author = author.Value; Title = title.Value; IsFavorite = false }
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