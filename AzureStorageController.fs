module WebBackend.AzureStorageController

open System
open System.IO

open Microsoft.Azure.Cosmos.Table

open Microsoft.Extensions.Logging

open FSharp.Azure.Storage.Table

open Domain


/// Common

let findStorageConnectionString (log: ILogger) : string =
    let connectionStringCandidate = Environment.GetEnvironmentVariable "StorageConnectionString"
    let connectionString =
        if String.IsNullOrWhiteSpace(connectionStringCandidate) then
            log.LogWarning "Unable to find Azure Storage Connection String, if this is a local development environment using Azurite, then this is of course correct."
            ""
        else
            connectionStringCandidate
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
    let inBookTable b = inTable tableClient "Books" b
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

let getRecipesFromTable (tableClient: CloudTableClient) : RecipeDTO list =
    let fromRecipeTable q = fromTable tableClient "Recipes" q
    let recipes =
        Query.all<RecipeDTO>
        |> fromRecipeTable
        |> Seq.map (fun (b,_) -> b)
        |> Seq.toList
    recipes

let findRecipesUsingName (tableClient: CloudTableClient) (name: string) (log: ILogger) : RecipeDTO list =
    let fromRecipeTable q = fromTable tableClient "Recipes" q
    log.LogInformation <| "Trying to find recipes with Name: '" + name + "'."
    let recipes =
        Query.all<RecipeDTO>
        |> Query.where <@ fun _ s -> s.PartitionKey = name @>
        |> fromRecipeTable
        |> Seq.map (fun (b,_) -> b)
        |> Seq.toList
    recipes

let insertRecipeInTable (tableClient: CloudTableClient) (recipe: RecipeDTO) (log: ILogger) : unit =
    let inRecipeTable r = inTable tableClient "Recipes" r
    try
        let result = recipe |> Insert |> inRecipeTable
        ignore <| match result.HttpStatusCode with
                    | 200 | 201 | 202 | 203 | 204 | 205 -> log.LogInformation <| "Recipe '" + recipe.ToString() + "' successfully inserted."
                    | code -> log.LogWarning <| "Could not insert recipe '" + recipe.ToString() + "'.\nHTTP Status: '" + code.ToString() + "'."
    with
        | :? StorageException as sx ->
            match sx.Message with
            | "Conflict" -> log.LogWarning <| "Insert failed due to conflicting Keys, PartitionKey: '" + recipe.Name + "', RowKey: '" + recipe.Json + "'."
            | _ -> log.LogWarning <| "Insert failed with exception:\n" + sx.ToString()
        | ex -> log.LogWarning <| "Insert failed with exception:\n" + ex.ToString()
