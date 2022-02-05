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

module Meals =
    type CommandJson =
        {
            Action: string option
        }

    type RecipeJson =
        {
            Name: string option
            Link: string option
            Portions: int option
        }

    type ResponseJson =
        {
            Recipes: Recipe list
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

    let getRecipeFromReqBody (body: string) (log: ILogger) : RecipeJson =
        try
            let recipe = Json.deserialize<RecipeJson> body
            recipe
        with
            ex ->
                log.LogWarning <| "Get Recipe failed with exception:\n" + ex.ToString()
                { Name = None; Link = None; Portions = None }

    [<FunctionName("Meals")>]
    let run ([<HttpTrigger(AuthorizationLevel.Function, "post", Route = null)>]req: HttpRequest) (log: ILogger) =
        task {
            log.LogInformation("WebBackend.Meals received a POST request")

            use stream = new StreamReader(req.Body)
            let! reqBody = stream.ReadToEndAsync() |> Async.AwaitTask

            let commandRes = getCommandFromReqBody reqBody log
            let recipeRes = getRecipeFromReqBody reqBody log

            let connectionString = findStorageConnectionString log
            let tableClient = initTableClient connectionString

            let response =
                match commandRes.Action with
                | Some "Find" ->
                    match recipeRes.Name with
                    | None ->
                        let message = "No 'Name' specified, trying to return all recipes."
                        log.LogInformation message
                        let recipes = getRecipesFromTable tableClient
                        match recipes with
                        | [] ->
                            let error = "Could not find any recipes at all."
                            log.LogWarning error
                            { Recipes = []; Error = Some error }
                        | recipes -> { Recipes = recipes; Error = None }
                    | Some name ->
                        let recipes = findRecipesUsingName tableClient name log
                        match recipes with
                        | [] ->
                            let error = "Could not find any recipes with Name: '" + name + "'."
                            log.LogWarning error
                            { Recipes = []; Error = Some error }
                        | recipes -> { Recipes = recipes; Error = None }
                | Some "Insert" ->
                    match recipeRes.Name with
                    | None ->
                        let error = "Could not Insert recipe without a 'Name'."
                        log.LogWarning error
                        { Recipes = []; Error = Some error }
                    | Some name ->
                        let recipe =
                            let link = if recipeRes.Link.IsSome then recipeRes.Link.Value else ""
                            let portions = if recipeRes.Portions.IsSome then recipeRes.Portions.Value else 0
                            { Name = name; Link = link; Portions = portions } : Recipe
                        insertRecipeInTable tableClient recipe log
                        { Recipes = [recipe]; Error = None }
                | Some action ->
                    let error = "Unknown Action: '" + action + "'."
                    log.LogWarning error
                    { Recipes = []; Error = Some error }
                | None ->
                    let error = "No Action specified."
                    log.LogWarning error
                    { Recipes = []; Error = Some error }

            let result : IActionResult =
                match response with
                | { Recipes = _; Error = Some error } ->
                    BadRequestObjectResult error
                | { Recipes = books; Error = None } ->
                    OkObjectResult books
            return result
        }