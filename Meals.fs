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
open HttpJsonController
open AzureStorageController


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
            | Some Unknown -> { Recipes = []; Success = None; Error = None } // Action Unknown is for development/testing purposes only
            | Some FindRecipe ->
                let recipeNameJson = getRecipeNameFromReqBody reqBody log
                match recipeNameJson.RecipeName with
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
            | Some InsertRecipe ->
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
            | Some RemoveRecipe ->
                let recipeName = getRecipeNameFromReqBody reqBody log
                match recipeName.RecipeName with
                | None ->
                    let error = "Could not Remove recipe without a 'Name'."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error } : OperationResponse
                | Some name ->
                    let result = removeRecipeFromTable tableClient name log
                    match result with
                    | Ok message -> { Recipes = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Success = None; Error = Some error } : OperationResponse
            | Some ChangeRecipeName ->
                let recipeName = getRecipeNameFromReqBody reqBody log
                let newRecipeName = getNewRecipeNameFromReqBody reqBody log
                match (recipeName.RecipeName, newRecipeName.NewRecipeName) with
                | (None, _) ->
                    let error = "No recipe to change found."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (_, None) ->
                    let error = "No new recipe name specified."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (Some recipeName, Some newRecipeName) ->
                    let result = updateRecipeWithNewName tableClient recipeName newRecipeName log
                    match result with
                    | Ok message -> { Recipes = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Success = None; Error = Some error } : OperationResponse
            | Some UpdateRecipeLink -> { Recipes = []; Success = None; Error = None } // Not yet implemented
            | Some ChangeRecipePortions -> { Recipes = []; Success = None; Error = None } // Not yet implemented
            | Some AddIngredientToRecipe ->
                let ingredient = getIngredientFromReqBody reqBody log
                let recipeName = getRecipeNameFromReqBody reqBody log
                match (ingredient.Product.Name, recipeName.RecipeName) with
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
            | Some AddInstructionToRecipe ->
                let instruction = getInstructionFromReqBody reqBody log
                let recipeName = getRecipeNameFromReqBody reqBody log
                match (instruction.Instruction, recipeName.RecipeName) with
                | ("", _) ->
                    let error = "Invalid instruction."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (_, None) ->
                    let error = "No recipe specified."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (_, Some recipeName) ->
                    let result = addInstructionToRecipe tableClient instruction recipeName log
                    match result with
                    | Ok message -> { Recipes = []; Success = Some message; Error = None }
                    | Error error -> { Recipes = []; Success = None; Error = Some error }
            | Some AddCommentToRecipe ->
                let comment = getCommentFromReqBody reqBody log
                let recipeName = getRecipeNameFromReqBody reqBody log
                match (comment.Comment, recipeName.RecipeName) with
                | ("", _) ->
                    let error = "Invalid comment."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (_, None) ->
                    let error = "No recipe specified."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (_, Some recipeName) ->
                    let result = addCommentToRecipe tableClient comment recipeName log
                    match result with
                    | Ok message -> { Recipes = []; Success = Some message; Error = None }
                    | Error error -> { Recipes = []; Success = None; Error = Some error }
            | Some RemoveIngredientFromRecipe ->
                let ingredient = getIngredientNameFromReqBody reqBody log
                let recipeName = getRecipeNameFromReqBody reqBody log
                match (ingredient.IngredientName, recipeName.RecipeName) with
                | (None, _) ->
                    let error = "Invalid ingredient."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (_, None) ->
                    let error = "No recipe specified."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (Some ingredientName, Some recipeName) ->
                    let result = removeIngredientFromRecipe tableClient ingredientName recipeName log
                    match result with
                    | Ok message -> { Recipes = []; Success = Some message; Error = None }
                    | Error error -> { Recipes = []; Success = None; Error = Some error }
            | Some RemoveInstructionFromRecipe ->
                let instruction = getInstructionFromReqBody reqBody log
                let recipeName = getRecipeNameFromReqBody reqBody log
                match (instruction.Instruction, recipeName.RecipeName) with
                | ("", _) ->
                    let error = "Invalid instruction."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (_, None) ->
                    let error = "No recipe specified."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (_, Some recipeName) ->
                    let result = removeInstructionFromRecipe tableClient instruction recipeName log
                    match result with
                    | Ok message -> { Recipes = []; Success = Some message; Error = None }
                    | Error error -> { Recipes = []; Success = None; Error = Some error }
            | Some RemoveCommentFromRecipe ->
                let comment = getCommentFromReqBody reqBody log
                let recipeName = getRecipeNameFromReqBody reqBody log
                match (comment.Comment, recipeName.RecipeName) with
                | ("", _) ->
                    let error = "Invalid comment."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (_, None) ->
                    let error = "No recipe specified."
                    log.LogWarning error
                    { Recipes = []; Success = None; Error = Some error }
                | (_, Some recipeName) ->
                    let result = removeCommentFromRecipe tableClient comment recipeName log
                    match result with
                    | Ok message -> { Recipes = []; Success = Some message; Error = None }
                    | Error error -> { Recipes = []; Success = None; Error = Some error }
            // | Some action ->
            //     let error = "Action: '" + action.ToString() + "' not yet implemented."
            //     log.LogWarning error
            //     { Recipes = []; Success = None; Error = Some error } : OperationResponse
            | None ->
                let error = "Could not identify a supported Action."
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