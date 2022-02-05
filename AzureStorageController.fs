namespace WebBackend

open System
open System.IO

open Microsoft.Azure.Cosmos.Table

open Microsoft.Extensions.Logging

open FSharp.Azure.Storage.Table

module AzureStorageController =

    type Book =
        {
            [<PartitionKey>] Author: string
            [<RowKey>] Title: string
            IsFavorite: bool
        }

    type Recipe =
        {
            [<PartitionKey>] Name: string
            [<RowKey>] Link: string
            Portions: int
        }

    /// Common

    let findStorageConnectionString (log: ILogger) : string =
        log.LogInformation <| "Trying to find Azure Storage Connection String"
        let connectionStringCandidate = Environment.GetEnvironmentVariable "StorageConnectionString"
        log.LogInformation <| "Found this connection string: '" + connectionStringCandidate + "'."
        let connectionString = if String.IsNullOrWhiteSpace(connectionStringCandidate) then "" else connectionStringCandidate
        connectionString

    let initTableClient (connectionString: string) : CloudTableClient =
        let actualConnectionString =
            match connectionString with
            | "" -> "UseDevelopmentStorage=true;"
            | s -> s
        let account = CloudStorageAccount.Parse actualConnectionString
        let tableClient = account.CreateCloudTableClient()
        tableClient
 
    /// Books

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

    /// Recipes

    let getRecipesFromTable (tableClient: CloudTableClient) : Recipe list =
        let fromRecipeTable q = fromTable tableClient "Recipes" q
        let recipes =
            Query.all<Recipe>
            |> fromRecipeTable
            |> Seq.map (fun (b,_) -> b)
            |> Seq.toList
        recipes

    let findRecipesUsingName (tableClient: CloudTableClient) (name: string) (log: ILogger) : Recipe list =
        let fromRecipeTable q = fromTable tableClient "Recipes" q
        log.LogInformation <| "Trying to find recipes with Name: '" + name + "'."
        let recipes =
            Query.all<Recipe>
            |> Query.where <@ fun _ s -> s.PartitionKey = name @>
            |> fromRecipeTable
            |> Seq.map (fun (b,_) -> b)
            |> Seq.toList
        recipes

    let insertRecipeInTable (tableClient: CloudTableClient) (recipe: Recipe) (log: ILogger) : unit =
        let inRecipeTable book = inTable tableClient "Recipes" book
        try
            let result = recipe |> Insert |> inRecipeTable
            ignore <| match result.HttpStatusCode with
                      | 200 | 201 | 202 | 203 | 204 | 205 -> log.LogInformation <| "Recipe '" + recipe.ToString() + "' successfully inserted."
                      | code -> log.LogWarning <| "Could not insert recipe '" + recipe.ToString() + "'.\nHTTP Status: '" + code.ToString() + "'."
        with
            | :? StorageException as sx ->
                match sx.Message with
                | "Conflict" -> log.LogWarning <| "Insert failed due to conflicting Keys, PartitionKey: '" + recipe.Name + "', RowKey: '" + recipe.Link + "'."
                | _ -> log.LogWarning <| "Insert failed with exception:\n" + sx.ToString()
            | ex -> log.LogWarning <| "Insert failed with exception:\n" + ex.ToString()
