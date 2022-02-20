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

type Action
    = Unknown
    | Find
    | Insert
    | Remove
    | AddIngredient
    | AddInstruction

type CommandJson =
    {
        Action: Action option
    }

type RecipeNameJson =
    {
        Name: string option
    }

type OperationResponse =
    {
        Recipes: Recipe list
        Success: string option
        Error: string option
    }

type ResponseJson =
    {
        Message: string option
        Recipes: Recipe list
    }

let getCommandFromReqBody' (body: string) (log: ILogger) : CommandJson =
    try
        let command = Json.deserialize<CommandJson> body
        command
    with
        ex ->
            log.LogWarning <| "Get Command failed with exception:\n" + ex.ToString()
            { Action = None }

let getCommandFromReqBody (body: string) (log: ILogger) : Action option =
    try
        let command = Json.deserialize<CommandJson> body
        command.Action
    with
        ex ->
            log.LogWarning <| "Get Command failed with exception:\n" + ex.ToString()
            None

let getRecipeFromReqBody (body: string) (log: ILogger) : Recipe =
    try
        let recipe = Json.deserialize<Recipe> body
        recipe
    with
        ex ->
            log.LogWarning <| "Get Recipe failed with exception:\n" + ex.ToString()
            { Name = ""; Link = None; Portions = 0; Ingredients = []; Instructions = []; TastingNotes = [] }

let getProductFromReqBody (body: string) (log: ILogger) : Product =
    try
        let product = Json.deserialize<Product> body
        product
    with
        ex ->
            log.LogWarning <| "Get Product failed with exception:\n" + ex.ToString()
            { Name = ""; Link = None; Comments = [] }

let getIngredientFromReqBody (body: string) (log: ILogger) : Ingredient =
    try
        let ingredient = Json.deserialize<Ingredient> body
        ingredient
    with
        ex ->
            log.LogWarning <| "Get Ingredient failed with exception:\n" + ex.ToString()
            { Product = { Name = ""; Link = None; Comments = [] }; Quantity = { Amount = 0.0; Unit = NotDefined } }

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

        let connectionString = findStorageConnectionString log
        let tableClient = initTableClient connectionString

        let response =
            match commandRes with
            | Some Find ->
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
                        { Recipes = []; Success = None; Error = Some error } : OperationResponse
                    | recipeDTOs ->
                        let recipes = List.map (fun { Name = _; NameAgain = _; Json = json } -> Json.deserialize<Recipe> json) recipeDTOs
                        { Recipes = recipes; Success = Some "Successfully retrieved all recipes."; Error = None } : OperationResponse
                | Some name ->
                    let recipeDTOs = findRecipesUsingName tableClient name log
                    match recipeDTOs with
                    | [] ->
                        let error = "Could not find any recipes with Name: '" + name + "'."
                        log.LogWarning error
                        { Recipes = []; Success = None; Error = Some error } : OperationResponse
                    | recipeDTOs ->
                        let recipes = List.map (fun { Name = _; NameAgain = _; Json = json } -> Json.deserialize<Recipe> json) recipeDTOs
                        { Recipes = recipes; Success = Some "Successfully retrieved recipes."; Error = None } : OperationResponse
            | Some Insert ->
                let recipe = getRecipeFromReqBody reqBody log
                match recipe.Name with
                | "" ->
                    let error = "Could not Insert recipe without a 'Name'."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error } : OperationResponse
                | name ->
                    let json = Json.serialize recipe
                    let result = insertRecipeInTable tableClient { Name = name; NameAgain = name; Json = json } log
                    match result with
                    | Ok message -> { Recipes = [recipe]; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = [recipe]; Success = None; Error = Some error } : OperationResponse
            | Some Remove ->
                let recipeName = getRecipeNameFromReqBody reqBody log
                match recipeName.Name with
                | None ->
                    let error = "Could not Remove recipe without a 'Name'."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error } : OperationResponse
                | Some name ->
                    let result = removeRecipeFromTable tableClient name log
                    match result with
                    | Ok message -> { Recipes = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Success = None; Error = Some error } : OperationResponse
            | Some AddIngredient ->
                let ingredient = getIngredientFromReqBody reqBody log
                let recipeName = getRecipeNameFromReqBody reqBody log
                match (ingredient.Product.Name, recipeName.Name) with
                | ("", _) ->
                    let error = "Invalid ingredient."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (_, None) ->
                    let error = "No recipe specified."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (productName, Some recipeName) ->
                    let result = addIngredientToRecipe tableClient ingredient recipeName log
                    match result with
                    | Ok message -> { Recipes = []; Success = Some message; Error = None }
                    | Error error -> { Recipes = []; Success = None; Error = Some error }
            | Some action ->
                let error = "Unknown Action: '" + action.ToString() + "'."
                log.LogWarning error
                { Recipes = []; Success = None; Error = Some error } : OperationResponse
            | None ->
                let error = "No Action specified."
                log.LogWarning error
                { Recipes = []; Success = None; Error = Some error } : OperationResponse

        let result : IActionResult =
            match response with
            | { Recipes = recipes; Success = None; Error = Some error } ->
                let errorResponse = Json.serialize { Message = response.Error; Recipes = recipes }
                let logMessage = sprintf "Something went wrong, these were the recipes: '%A', and this was the error message: '%s'" recipes error
                log.LogWarning logMessage
                BadRequestObjectResult errorResponse
            | { Recipes = recipes; Success = Some message; Error = None } ->
                let successResponse = Json.serialize { Message = Some message; Recipes = recipes }
                log.LogInformation "It looks like everything went well."
                OkObjectResult successResponse
            | { Recipes = _; Success = _; Error = _ } ->
                let errorMessage = "Hmm, something is not configured correctly."
                let otherResponse = Json.serialize { Message = Some errorMessage; Recipes = [] }
                log.LogWarning errorMessage
                BadRequestObjectResult otherResponse
        return result
    }