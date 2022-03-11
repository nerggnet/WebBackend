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
            | Some Unknown -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = None } // Action Unknown is for development/testing purposes only
            // Recipe actions
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
                        { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                    | recipeDTOs ->
                        let recipes = List.map (fun ({ Name = _; NameAgain = _; Json = json } : RecipeDTO) -> Json.deserialize<Recipe> json) recipeDTOs
                        { Recipes = recipes; Menus = []; ShoppingLists = []; Success = Some "Successfully retrieved all recipes."; Error = None } : OperationResponse
                | Some name ->
                    let recipeDTOs = findRecipesUsingName tableClient name log
                    match recipeDTOs with
                    | [] ->
                        let error = "Could not find any recipes with Name: '" + name + "'."
                        log.LogWarning error
                        { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                    | recipeDTOs ->
                        let recipes = List.map (fun ({ Name = _; NameAgain = _; Json = json } : RecipeDTO) -> Json.deserialize<Recipe> json) recipeDTOs
                        { Recipes = recipes; Menus = []; ShoppingLists = []; Success = Some "Successfully retrieved recipes."; Error = None } : OperationResponse
            | Some InsertRecipe ->
                let recipe = getRecipeFromReqBody reqBody log
                match recipe.Name with
                | "" ->
                    let error = "Could not Insert recipe without a 'Name'."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | name ->
                    let json = Json.serialize recipe
                    let result = insertRecipeInTable tableClient { Name = name; NameAgain = name; Json = json } log
                    match result with
                    | Ok message -> { Recipes = [recipe]; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = [recipe]; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some RemoveRecipe ->
                let recipeName = getRecipeNameFromReqBody reqBody log
                match recipeName.RecipeName with
                | None ->
                    let error = "Could not Remove recipe without a 'RecipeName'."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | Some name ->
                    let result = removeRecipeFromTable tableClient name log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some ChangeRecipeName ->
                let recipeName = getRecipeNameFromReqBody reqBody log
                let newRecipeName = getNewRecipeNameFromReqBody reqBody log
                match (recipeName.RecipeName, newRecipeName.NewRecipeName) with
                | (None, _) ->
                    let error = "No recipe to change found."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No new recipe name specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (Some recipeName, Some newRecipeName) ->
                    let result = updateRecipeWithNewName tableClient recipeName newRecipeName log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some UpdateRecipeLink ->
                let recipeName = getRecipeNameFromReqBody reqBody log
                let link = getLinkFromReqBody reqBody log
                match (recipeName.RecipeName, link.Link) with
                | (None, _) ->
                    let error = "No recipe to change found."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No new link specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (Some recipeName, Some httpLink) ->
                    let result = updateRecipeWithNewLink tableClient recipeName httpLink log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some ChangeRecipePortions ->
                let recipeName = getRecipeNameFromReqBody reqBody log
                let portions = getPortionsFromReqBody reqBody log
                match (recipeName.RecipeName, portions.Portions) with
                | (None, _) ->
                    let error = "No recipe to change found."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No new portion size specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (Some recipeName, Some portions) ->
                    let result = updateRecipeWithNewPortions tableClient recipeName portions log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some ChangeRecipeBaseInfo ->
                let recipeName = getRecipeNameFromReqBody reqBody log
                let newRecipeName = getNewRecipeNameFromReqBody reqBody log
                let portions = getPortionsFromReqBody reqBody log
                let link = getLinkFromReqBody reqBody log
                match (recipeName.RecipeName, newRecipeName.NewRecipeName, portions.Portions, link.Link) with
                | (None, _, _, _) ->
                    let error = "No recipe to change found."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None, _, _) ->
                    let error = "No new recipe name specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, _, None, _) ->
                    let error = "No new portion size specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, _, _, None) ->
                    let error = "No new link specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (Some recipeName, Some newRecipeName, Some portions, Some httpLink) ->
                    let result = updateRecipeWithNewBaseInfo tableClient recipeName newRecipeName portions httpLink log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse

            | Some AddIngredientToRecipe ->
                let ingredient = getIngredientFromReqBody reqBody log
                let recipeName = getRecipeNameFromReqBody reqBody log
                match (ingredient.Product.Name, recipeName.RecipeName) with
                | ("", _) ->
                    let error = "Invalid ingredient."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No recipe specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (productName, Some recipeName) ->
                    let result = addIngredientToRecipe tableClient ingredient recipeName log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some AddInstructionToRecipe ->
                let instruction = getInstructionFromReqBody reqBody log
                let recipeName = getRecipeNameFromReqBody reqBody log
                match (instruction.Instruction, recipeName.RecipeName) with
                | ("", _) ->
                    let error = "Invalid instruction."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No recipe specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, Some recipeName) ->
                    let result = addInstructionToRecipe tableClient instruction recipeName log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some AddCommentToRecipe ->
                let comment = getCommentFromReqBody reqBody log
                let recipeName = getRecipeNameFromReqBody reqBody log
                match (comment.Comment, recipeName.RecipeName) with
                | ("", _) ->
                    let error = "Invalid comment."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No recipe specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, Some recipeName) ->
                    let result = addCommentToRecipe tableClient comment recipeName log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some RemoveIngredientFromRecipe ->
                let ingredient = getIngredientNameFromReqBody reqBody log
                let recipeName = getRecipeNameFromReqBody reqBody log
                match (ingredient.IngredientName, recipeName.RecipeName) with
                | (None, _) ->
                    let error = "Invalid ingredient."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No recipe specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (Some ingredientName, Some recipeName) ->
                    let result = removeIngredientFromRecipe tableClient ingredientName recipeName log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some RemoveInstructionFromRecipe ->
                let instruction = getInstructionFromReqBody reqBody log
                let recipeName = getRecipeNameFromReqBody reqBody log
                match (instruction.Instruction, recipeName.RecipeName) with
                | ("", _) ->
                    let error = "Invalid instruction."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No recipe specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, Some recipeName) ->
                    let result = removeInstructionFromRecipe tableClient instruction recipeName log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some RemoveCommentFromRecipe ->
                let comment = getCommentFromReqBody reqBody log
                let recipeName = getRecipeNameFromReqBody reqBody log
                match (comment.Comment, recipeName.RecipeName) with
                | ("", _) ->
                    let error = "Invalid comment."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No recipe specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, Some recipeName) ->
                    let result = removeCommentFromRecipe tableClient comment recipeName log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            // Menu actions
            | Some FindMenu ->
                let menuNameJson = getMenuNameFromReqBody reqBody log
                match menuNameJson.MenuName with
                | None ->
                    let message = "No 'Name' specified, trying to return all menus."
                    log.LogInformation message
                    let menuDTOs = getMenusFromTable tableClient
                    match menuDTOs with
                    | [] ->
                        let error = "Could not find any menus at all."
                        log.LogWarning error
                        { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                    | menuDTOs ->
                        let menus = List.map (fun ({ Name = _; NameAgain = _; Json = json } : MenuDTO) -> Json.deserialize<Menu> json) menuDTOs
                        { Recipes = []; Menus = menus; ShoppingLists = []; Success = Some "Successfully retrieved all menus."; Error = None } : OperationResponse
                | Some name ->
                    let menuDTOs = findMenusUsingName tableClient name log
                    match menuDTOs with
                    | [] ->
                        let error = "Could not find any menus with Name: '" + name + "'."
                        log.LogWarning error
                        { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                    | menuDTOs ->
                        let menus = List.map (fun ({ Name = _; NameAgain = _; Json = json }: MenuDTO) -> Json.deserialize<Menu> json) menuDTOs
                        { Recipes = []; Menus = menus; ShoppingLists = []; Success = Some "Successfully retrieved menus."; Error = None } : OperationResponse
            | Some InsertMenu ->
                let menu = getMenuFromReqBody reqBody log
                match menu.Name with
                | "" ->
                    let error = "Could not Insert menu without a 'Name'."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | name ->
                    let json = Json.serialize menu
                    let result = insertMenuInTable tableClient { Name = name; NameAgain = name; Json = json } log
                    match result with
                    | Ok message -> { Recipes = []; Menus = [menu]; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = [menu]; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some RemoveMenu ->
                let menuName = getMenuNameFromReqBody reqBody log
                match menuName.MenuName with
                | None ->
                    let error = "Could not Remove menu without a 'MenuName'."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | Some name ->
                    let result = removeMenuFromTable tableClient name log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some ChangeMenuName ->
                let menuName = getMenuNameFromReqBody reqBody log
                let newMenuName = getNewMenuNameFromReqBody reqBody log
                match (menuName.MenuName, newMenuName.NewMenuName) with
                | (None, _) ->
                    let error = "No menu to change found."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No new menu name specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (Some menuName, Some newMenuName) ->
                    let result = updateMenuWithNewName tableClient menuName newMenuName log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some AddItemToMenu ->
                let menuItem = getMenuItemFromReqBody reqBody log
                let menuName = getMenuNameFromReqBody reqBody log
                match (menuItem.RecipeName, menuName.MenuName) with
                | ("", _) ->
                    let error = "Invalid menuItem."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No menu specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, Some menuName) ->
                    let result = addItemToMenu tableClient menuItem menuName log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some RemoveItemFromMenu ->
                let menuItem = getMenuItemFromReqBody reqBody log
                let menuName = getMenuNameFromReqBody reqBody log
                match (menuItem.RecipeName, menuName.MenuName) with
                | ("", _) ->
                    let error = "Invalid menuItem."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No menu specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, Some menuName) ->
                    let result = removeItemFromMenu tableClient menuItem.RecipeName menuItem.WeekDay menuName log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            // Shopping list actions
            | Some FindShoppingList ->
                let shoppingListNameJson = getShoppingListNameFromReqBody reqBody log
                match shoppingListNameJson.ShoppingListName with
                | None ->
                    let message = "No 'Name' specified, trying to return all shopping lists."
                    log.LogInformation message
                    let shoppingListDTOs = getShoppingListsFromTable tableClient
                    match shoppingListDTOs with
                    | [] ->
                        let error = "Could not find any shopping lists at all."
                        log.LogWarning error
                        { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                    | shoppingListDTOs ->
                        let shoppingLists = List.map (fun ({ Name = _; NameAgain = _; Json = json } : ShoppingListDTO) -> Json.deserialize<ShoppingList> json) shoppingListDTOs
                        { Recipes = []; Menus = []; ShoppingLists = shoppingLists; Success = Some "Successfully retrieved all shopping lists."; Error = None } : OperationResponse
                | Some name ->
                    let shoppingListDTOs = findShoppingListsUsingName tableClient name log
                    match shoppingListDTOs with
                    | [] ->
                        let error = "Could not find any shoppingLists with Name: '" + name + "'."
                        log.LogWarning error
                        { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                    | shoppingListDTOs ->
                        let shoppingLists = List.map (fun ({ Name = _; NameAgain = _; Json = json }: ShoppingListDTO) -> Json.deserialize<ShoppingList> json) shoppingListDTOs
                        { Recipes = []; Menus = []; ShoppingLists = shoppingLists; Success = Some "Successfully retrieved shopping lists."; Error = None } : OperationResponse
            | Some InsertShoppingList ->
                let shoppingList = getShoppingListFromReqBody reqBody log
                match shoppingList.Name with
                | "" ->
                    let error = "Could not Insert shopping list without a 'Name'."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | name ->
                    let json = Json.serialize shoppingList
                    let result = insertShoppingListInTable tableClient ({ Name = name; NameAgain = name; Json = json } : ShoppingListDTO) log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = [shoppingList]; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = [shoppingList]; Success = None; Error = Some error } : OperationResponse
            | Some RemoveShoppingList ->
                let shoppingListName = getShoppingListNameFromReqBody reqBody log
                match shoppingListName.ShoppingListName with
                | None ->
                    let error = "Could not Remove shopping list without a 'ShoppingListName'."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | Some name ->
                    let result = removeShoppingListFromTable tableClient name log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some ChangeShoppingListName ->
                let shoppingListName = getShoppingListNameFromReqBody reqBody log
                let newShoppingListName = getNewShoppingListNameFromReqBody reqBody log
                match (shoppingListName.ShoppingListName, newShoppingListName.NewShoppingListName) with
                | (None, _) ->
                    let error = "No shopping list to change found."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No new shopping list name specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (Some shoppingListName, Some newShoppingListName) ->
                    let result = updateShoppingListWithNewName tableClient shoppingListName newShoppingListName log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some AddItemToShoppingList ->
                let shoppingItem = getShoppingItemFromReqBody reqBody log
                let shoppingListName = getShoppingListNameFromReqBody reqBody log
                match (shoppingItem.Name, shoppingListName.ShoppingListName) with
                | ("", _) ->
                    let error = "Invalid shopping item."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No shopping list specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, Some shoppingListName) ->
                    let result = addItemToShoppingList tableClient shoppingItem shoppingListName log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | Some RemoveItemFromShoppingList ->
                let shoppingItemName = getShoppingItemNameFromReqBody reqBody log
                let shoppingListName = getShoppingListNameFromReqBody reqBody log
                match (shoppingItemName.ShoppingItemName, shoppingListName.ShoppingListName) with
                | (None, _) ->
                    let error = "Invalid shopping item."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (_, None) ->
                    let error = "No shopping list specified."
                    log.LogWarning error
                    { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
                | (Some shoppingItemName, Some shoppingListName) ->
                    let result = removeItemFromShoppingList tableClient shoppingItemName shoppingListName log
                    match result with
                    | Ok message -> { Recipes = []; Menus = []; ShoppingLists = []; Success = Some message; Error = None } : OperationResponse
                    | Error error -> { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error } : OperationResponse
            | None ->
                let error = "Could not identify a supported Action."
                log.LogWarning error
                { Recipes = []; Menus = []; ShoppingLists = []; Success = None; Error = Some error }

        let result : IActionResult =
            match response with
            | { Recipes = recipes; Menus = menus; ShoppingLists = shoppingLists; Success = None; Error = Some error } ->
                let errorResponse = Json.serialize { Message = response.Error; Recipes = recipes; Menus = menus; ShoppingLists = shoppingLists }
                let logMessage = sprintf "Something went wrong, these were the recipes: '%A', and this was the error message: '%s'" recipes error
                log.LogWarning logMessage
                BadRequestObjectResult errorResponse
            | { Recipes = recipes; Menus = menus; ShoppingLists = shoppingLists; Success = Some message; Error = None } ->
                let successResponse = Json.serialize { Message = Some message; Recipes = recipes; Menus = menus; ShoppingLists = shoppingLists }
                log.LogInformation "It looks like everything went well."
                OkObjectResult successResponse
            | { Recipes = _; Menus = _; ShoppingLists = _; Success = _; Error = _ } ->
                let errorMessage = "Hmm, something is not configured correctly."
                let otherResponse = Json.serialize { Message = Some errorMessage; Recipes = []; Menus = []; ShoppingLists = [] }
                log.LogWarning errorMessage
                BadRequestObjectResult otherResponse
        return result
    }
