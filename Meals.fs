module WebBackend.Meals

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

type RecipeNameJson =
    {
        Name: string option
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

let getRecipeFromReqBody (body: string) (log: ILogger) : Recipe =
    try
        let recipe = Json.deserialize<Recipe> body
        recipe
    with
        ex ->
            log.LogWarning <| "Get Recipe failed with exception:\n" + ex.ToString()
            { Name = ""; Link = None; Portions = 0; Ingredients = []; Instructions = []; TastingNotes = [] }

let getRecipeNameFromReqBody (body: string) (log: ILogger) : RecipeNameJson =
    try
        let recipeName = Json.deserialize<RecipeNameJson> body
        recipeName
    with
        ex ->
            log.LogWarning <| "Get Recipe failed with exception:\n" + ex.ToString()
            { Name = None }

[<FunctionName("Meals")>]
let run ([<HttpTrigger(AuthorizationLevel.Function, "post", Route = null)>]req: HttpRequest) (log: ILogger) =
    task {
        log.LogInformation("WebBackend.Meals received a POST request")

        use stream = new StreamReader(req.Body)
        let! reqBody = stream.ReadToEndAsync() |> Async.AwaitTask

        let commandRes = getCommandFromReqBody reqBody log
        //let recipeRes = getRecipeFromReqBody reqBody log

        let connectionString = findStorageConnectionString log
        let tableClient = initTableClient connectionString

        let response =
            match commandRes.Action with
            | Some "Find" ->
                let recipeNameJson = getRecipeNameFromReqBody reqBody log
                match recipeNameJson.Name with
                | None ->
                    let message = "No 'Name' specified, trying to return all recipes."
                    log.LogInformation message
                    let recipeDTOs = getRecipesFromTable tableClient
                    match recipeDTOs with
                    | [] ->
                        let error = "Could not find any recipes at all."
                        log.LogWarning error
                        { Recipes = []; Error = Some error }
                    | recipeDTOs ->
                        let recipes = List.map (fun { Name = _; Json = json } -> Json.deserialize<Recipe> json) recipeDTOs
                        { Recipes = recipes; Error = None }
                | Some name ->
                    let recipeDTOs = findRecipesUsingName tableClient name log
                    match recipeDTOs with
                    | [] ->
                        let error = "Could not find any recipes with Name: '" + name + "'."
                        log.LogWarning error
                        { Recipes = []; Error = Some error }
                    | recipeDTOs ->
                        let recipes = List.map (fun { Name = _; Json = json } -> Json.deserialize<Recipe> json) recipeDTOs
                        { Recipes = recipes; Error = None }
            | Some "Insert" ->
                let recipe = getRecipeFromReqBody reqBody log
                match recipe.Name with
                | "" ->
                    let error = "Could not Insert recipe without a 'Name'."
                    log.LogWarning error
                    { Recipes = []; Error = Some error }
                | name ->
                    let json = Json.serialize recipe
                    insertRecipeInTable tableClient { Name = name; Json = json } log
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
            | { Recipes = recipes; Error = Some error } ->
                let logMessage = sprintf "Something went wrong, these were the recipes: '%A', and this was the error message: '%s'" recipes error
                log.LogWarning logMessage
                BadRequestObjectResult error
            | { Recipes = recipes; Error = None } ->
                let serializedRecipes = Json.serialize recipes
                let logMessage = sprintf "It looks like everything went well, these are the serialized recipes: '%A'" serializedRecipes
                log.LogInformation logMessage
                OkObjectResult serializedRecipes
        return result
    }