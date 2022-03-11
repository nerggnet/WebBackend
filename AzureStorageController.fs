module WebBackend.AzureStorageController

open System
open System.IO

open Microsoft.Azure.Cosmos.Table

open Microsoft.Extensions.Logging

open FSharp.Azure.Storage.Table

open FSharp.Json

open Domain


/// Common

let findStorageConnectionString (log: ILogger) : string =
    let connectionStringCandidate = Environment.GetEnvironmentVariable "StorageConnectionString"
    let connectionString =
        if String.IsNullOrWhiteSpace(connectionStringCandidate) then
            log.LogInformation "Unable to find Azure Storage Connection String, if this is a local development environment using Azurite, then this is of course correct."
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


/// Recipes

type RecipeDTO =
    {
        [<PartitionKey>] Name : RecipeName
        [<RowKey>] NameAgain : RecipeName
        Json : string
    }

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

let insertRecipeInTable (tableClient: CloudTableClient) (recipe: RecipeDTO) (log: ILogger) : Result<string, string> =
    let inRecipeTable r = inTable tableClient "Recipes" r
    try
        let result = recipe |> Insert |> inRecipeTable
        match result.HttpStatusCode with
        | 200 | 201 | 202 | 203 | 204 | 205 ->
            let message = "Recipe '" + recipe.Name.ToString() + "' successfully inserted."
            log.LogInformation message
            Ok message
        | code ->
            let error = "Could not insert recipe '" + recipe.ToString() + "'.\nHTTP Status: '" + code.ToString() + "'."
            log.LogWarning error
            Error error
    with
        | :? StorageException as sx ->
            match sx.Message with
            | "Conflict" ->
                let error = "Insert failed due to conflicting Keys, a recipe with name '" + recipe.Name + "' already exists."
                log.LogWarning error
                Error error
            | _ ->
                let error = "Insert failed with exception:\n" + sx.ToString()
                log.LogWarning error
                Error error
        | ex ->
            let error = "Insert failed with exception:\n" + ex.ToString()
            log.LogWarning error
            Error error

let removeRecipeFromTable (tableClient: CloudTableClient) (name: string) (log: ILogger) : Result<string, string> =
    let inRecipeTable r = inTable tableClient "Recipes" r
    try
        let result = { EntityIdentifier.PartitionKey = name; RowKey = name } |> ForceDelete |> inRecipeTable
        match result.HttpStatusCode with
        | 200 | 201 | 202 | 203 | 204 | 205 ->
            let message = "Recipe '" + name + "' successfully removed."
            log.LogInformation message
            Ok message
        | code ->
            let error = "Could not remove recipe '" + name + "'.\nHTTP Status: '" + code.ToString() + "'."
            log.LogWarning error
            Error error
    with
        | :? StorageException as sx ->
            match sx.Message with
            | "Not Found" ->
                let error = "Remove failed due to that a recipe with name: '" + name + "' could not be found."
                log.LogWarning error
                Error error
            | _ ->
                let error = "Remove failed with exception:\n" + sx.ToString()
                log.LogWarning error
                Error error
        | ex ->
            let error = "Remove failed with exception:\n" + ex.ToString()
            log.LogWarning error
            Error error

let getRecipeForManipulation (tableClient: CloudTableClient) (recipeName: RecipeName) (log: ILogger) : Result<(Recipe * string), string> =
    let fromRecipeTable q = fromTable tableClient "Recipes" q
    let inRecipeTable r = inTable tableClient "Recipes" r
    try
        let queryResults =
            Query.all<RecipeDTO>
            |> Query.where <@ fun _ s -> s.PartitionKey = recipeName && s.RowKey = recipeName @>
            |> fromRecipeTable
            |> Seq.toList
        match queryResults with
        | [] ->
            let error = "Get recipe for manipulation failed since there is no recipe with name: '" + recipeName + "'."
            log.LogWarning error
            Error error
        | queryResults ->
            try
                let (recipeDTO, metaData) = queryResults.Head
                let recipe = Json.deserialize<Recipe> recipeDTO.Json
                let etag = metaData.Etag
                Ok (recipe, etag)
            with
                | ex ->
                    let error = "Get recipe for manipulation failed in deserialization, with exception:\n" + ex.ToString()
                    log.LogWarning error
                    Error error
    with
        | ex ->
            let error = "Get recipe for manipulation failed in query, with exception:\n" + ex.ToString()
            log.LogWarning error
            Error error

let storeUpdatedRecipe (tableClient: CloudTableClient) (recipeDTO: RecipeDTO) (etag: string) (log: ILogger) : Result<string, string> =
    let inRecipeTable r = inTable tableClient "Recipes" r
    try
        let replaceResult = (recipeDTO, etag) |> Replace |> inRecipeTable
        match replaceResult.HttpStatusCode with
        | 200 | 201 | 202 | 203 | 204 | 205 ->
            let message = "Store updated recipe '" + recipeDTO.Name + "' was successful."
            log.LogInformation message
            Ok message
        | code ->
            let error = "Store updated recipe '" + recipeDTO.Name + "' failed.\nHTTP Status: '" + code.ToString() + "'."
            log.LogWarning error
            Error error
    with
        | ex ->
            let error = "Store updated recipe failed with exception:\n" + ex.ToString()
            log.LogWarning error
            Error error

let addIngredientToRecipe (tableClient: CloudTableClient) (ingredient: Ingredient) (recipeName: RecipeName) (log: ILogger) : Result<string, string> =
    let getResult = getRecipeForManipulation tableClient recipeName log
    match getResult with
    | Ok (recipe, etag) ->
        let ingredients = recipe.Ingredients
        let alreadyInList = List.exists (fun elem -> elem.Product.Name = ingredient.Product.Name) ingredients
        match alreadyInList with
        | false ->
            let updatedIngredients = ingredients @ [ingredient]
            let updatedRecipe = { recipe with Ingredients = updatedIngredients }
            let updatedRecipeJson = Json.serialize updatedRecipe
            let updatedRecipeDTO = { Name = updatedRecipe.Name; NameAgain = updatedRecipe.Name; Json = updatedRecipeJson }
            let storeResult = storeUpdatedRecipe tableClient updatedRecipeDTO etag log
            match storeResult with
            | Ok _ ->
                let message = "Add ingredient '" + ingredient.Product.Name + "' to recipe '" + recipe.Name + "' was successful."
                log.LogInformation message
                Ok message
            | Error _ ->
                let error = "Add ingredient '" + ingredient.Product.Name + "' to recipe '" + recipe.Name + "' failed."
                log.LogWarning error
                Error error
        | true ->
            let error = "The same ingredient already exists in the recipe."
            log.LogWarning error
            Error error
    | Error _ ->            
        let error = "Add ingredient to recipe failed."
        log.LogWarning error
        Error error
 
let addInstructionToRecipe (tableClient: CloudTableClient) (instruction: Instruction) (recipeName: RecipeName) (log: ILogger) : Result<string, string> =
    let getResult = getRecipeForManipulation tableClient recipeName log
    match getResult with
    | Ok (recipe, etag) ->
        let instructions = recipe.Instructions
        let alreadyInList = List.exists (fun elem -> elem = instruction) instructions
        match alreadyInList with
        | false ->
            let updatedInstructions = instructions @ [instruction]
            let updatedRecipe = { recipe with Instructions = updatedInstructions }
            let updatedRecipeJson = Json.serialize updatedRecipe
            let updatedRecipeDTO = { Name = updatedRecipe.Name; NameAgain = updatedRecipe.Name; Json = updatedRecipeJson }
            let storeResult = storeUpdatedRecipe tableClient updatedRecipeDTO etag log
            match storeResult with
            | Ok _ ->
                let message = "Add instruction '" + instruction.Instruction + "' to recipe '" + recipe.Name + "' was successful."
                log.LogInformation message
                Ok message
            | Error _ ->
                let error = "Add instruction '" + instruction.Instruction + "' to recipe '" + recipe.Name + "' failed."
                log.LogWarning error
                Error error
        | true ->
            let error = "The same instruction already exists in the recipe."
            log.LogWarning error
            Error error
    | Error _ ->            
        let error = "Add instruction to recipe failed."
        log.LogWarning error
        Error error

let addCommentToRecipe (tableClient: CloudTableClient) (comment: Comment) (recipeName: RecipeName) (log: ILogger) : Result<string, string> =
    let getResult = getRecipeForManipulation tableClient recipeName log
    match getResult with
    | Ok (recipe, etag) ->
        let comments = recipe.Comments
        let alreadyInList = List.exists (fun elem -> elem = comment) comments
        match alreadyInList with
        | false ->
            let updatedComments = comments @ [comment]
            let updatedRecipe = { recipe with Comments = updatedComments }
            let updatedRecipeJson = Json.serialize updatedRecipe
            let updatedRecipeDTO = { Name = updatedRecipe.Name; NameAgain = updatedRecipe.Name; Json = updatedRecipeJson }
            let storeResult = storeUpdatedRecipe tableClient updatedRecipeDTO etag log
            match storeResult with
            | Ok _ ->
                let message = "Add comment '" + comment.Comment + "' to recipe '" + recipe.Name + "' was successful."
                log.LogInformation message
                Ok message
            | Error _ ->
                let error = "Add comment '" + comment.Comment + "' to recipe '" + recipe.Name + "' failed."
                log.LogWarning error
                Error error
        | true ->
            let error = "The same comment already exists in the recipe."
            log.LogWarning error
            Error error
    | Error _ ->            
        let error = "Add comment to recipe failed."
        log.LogWarning error
        Error error

let removeIngredientFromRecipe (tableClient: CloudTableClient) (ingredientName: ProductName) (recipeName: RecipeName) (log: ILogger) : Result<string, string> =
    let getResult = getRecipeForManipulation tableClient recipeName log
    match getResult with
    | Ok (recipe, etag) ->
        let ingredients = recipe.Ingredients
        let inList = List.exists (fun elem -> elem.Product.Name = ingredientName) ingredients
        match inList with
        | true ->
            let updatedIngredients = List.filter (fun elem -> elem.Product.Name <> ingredientName) ingredients
            let updatedRecipe = { recipe with Ingredients = updatedIngredients }
            let updatedRecipeJson = Json.serialize updatedRecipe
            let updatedRecipeDTO = { Name = updatedRecipe.Name; NameAgain = updatedRecipe.Name; Json = updatedRecipeJson }
            let storeResult = storeUpdatedRecipe tableClient updatedRecipeDTO etag log
            match storeResult with
            | Ok _ ->
                let message = "Remove ingredient '" + ingredientName + "' from recipe '" + recipe.Name + "' was successful."
                log.LogInformation message
                Ok message
            | Error _ ->
                let error = "Remove ingredient '" + ingredientName + "' from recipe '" + recipe.Name + "' failed."
                log.LogWarning error
                Error error
        | false ->
            let error = "The ingredient could not be found in the recipe."
            log.LogWarning error
            Error error
    | Error _ ->            
        let error = "Remove ingredient from recipe failed."
        log.LogWarning error
        Error error

let removeInstructionFromRecipe (tableClient: CloudTableClient) (instruction: Instruction) (recipeName: RecipeName) (log: ILogger) : Result<string, string> =
    let getResult = getRecipeForManipulation tableClient recipeName log
    match getResult with
    | Ok (recipe, etag) ->
        let instructions = recipe.Instructions
        let inList = List.exists (fun elem -> elem = instruction) instructions
        match inList with
        | true ->
            let updatedInstructions = List.filter (fun elem -> elem <> instruction) instructions
            let updatedRecipe = { recipe with Instructions = updatedInstructions }
            let updatedRecipeJson = Json.serialize updatedRecipe
            let updatedRecipeDTO = { Name = updatedRecipe.Name; NameAgain = updatedRecipe.Name; Json = updatedRecipeJson }
            let storeResult = storeUpdatedRecipe tableClient updatedRecipeDTO etag log
            match storeResult with
            | Ok _ ->
                let message = "Remove instruction '" + instruction.Instruction + "' from recipe '" + recipe.Name + "' was successful."
                log.LogInformation message
                Ok message
            | Error _ ->
                let error = "Remove instruction '" + instruction.Instruction + "' from recipe '" + recipe.Name + "' failed."
                log.LogWarning error
                Error error
        | false ->
            let error = "The instruction could not be found in the recipe."
            log.LogWarning error
            Error error
    | Error _ ->            
        let error = "Remove instruction from recipe failed."
        log.LogWarning error
        Error error

let removeCommentFromRecipe (tableClient: CloudTableClient) (comment: Comment) (recipeName: RecipeName) (log: ILogger) : Result<string, string> =
    let getResult = getRecipeForManipulation tableClient recipeName log
    match getResult with
    | Ok (recipe, etag) ->
        let comments = recipe.Comments
        let inList = List.exists (fun elem -> elem = comment) comments
        match inList with
        | true ->
            let updatedComments = List.filter (fun elem -> elem <> comment) comments
            let updatedRecipe = { recipe with Comments = updatedComments }
            let updatedRecipeJson = Json.serialize updatedRecipe
            let updatedRecipeDTO = { Name = updatedRecipe.Name; NameAgain = updatedRecipe.Name; Json = updatedRecipeJson }
            let storeResult = storeUpdatedRecipe tableClient updatedRecipeDTO etag log
            match storeResult with
            | Ok _ ->
                let message = "Remove comment '" + comment.Comment + "' from recipe '" + recipe.Name + "' was successful."
                log.LogInformation message
                Ok message
            | Error _ ->
                let error = "Remove comment '" + comment.Comment + "' from recipe '" + recipe.Name + "' failed."
                log.LogWarning error
                Error error
        | false ->
            let error = "The comment could not be found in the recipe."
            log.LogWarning error
            Error error
    | Error _ ->            
        let error = "Remove comment from recipe failed."
        log.LogWarning error
        Error error

let updateRecipeWithNewName (tableClient: CloudTableClient) (recipeName: RecipeName) (newRecipeName: RecipeName) (log: ILogger) : Result<string, string> =
    let getResult = getRecipeForManipulation tableClient recipeName log
    match getResult with
    | Ok (recipe, etag) ->
        let updatedRecipe = { recipe with Name = newRecipeName }
        let updatedRecipeJson = Json.serialize updatedRecipe
        let updatedRecipeDTO = { Name = updatedRecipe.Name; NameAgain = updatedRecipe.Name; Json = updatedRecipeJson }
        let storeResult = storeUpdatedRecipe tableClient updatedRecipeDTO etag log
        match storeResult with
        | Ok _ ->
            let message = "Update of recipe '" + recipe.Name + "' with new name '" + updatedRecipe.Name + "' was successful."
            log.LogInformation message
            Ok message
        | Error _ ->
            let error = "Update of recipe '" + recipeName + "' with new name '" + newRecipeName + "' failed."
            log.LogWarning error
            Error error
    | Error _ ->
        let error = "Update recipe with new name failed."
        log.LogWarning error
        Error error

let updateRecipeWithNewPortions (tableClient: CloudTableClient) (recipeName: RecipeName) (newPortions: Portions) (log: ILogger) : Result<string, string> =
    let getResult = getRecipeForManipulation tableClient recipeName log
    match getResult with
    | Ok (recipe, etag) ->
        let updatedRecipe = { recipe with Portions = newPortions }
        let updatedRecipeJson = Json.serialize updatedRecipe
        let updatedRecipeDTO = { Name = updatedRecipe.Name; NameAgain = updatedRecipe.Name; Json = updatedRecipeJson }
        let storeResult = storeUpdatedRecipe tableClient updatedRecipeDTO etag log
        match storeResult with
        | Ok _ ->
            let message = "Update of recipe '" + recipe.Name + "' with new portions '" + newPortions.ToString() + "' was successful."
            log.LogInformation message
            Ok message
        | Error _ ->
            let error = "Update of recipe '" + recipeName + "' with new portions '" + newPortions.ToString() + "' failed."
            log.LogWarning error
            Error error
    | Error _ ->
        let error = "Update recipe with new portions failed."
        log.LogWarning error
        Error error

let updateRecipeWithNewLink (tableClient: CloudTableClient) (recipeName: RecipeName) (newLink: HttpLink) (log: ILogger) : Result<string, string> =
    let getResult = getRecipeForManipulation tableClient recipeName log
    match getResult with
    | Ok (recipe, etag) ->
        let updatedRecipe = { recipe with Link = Some newLink }
        let updatedRecipeJson = Json.serialize updatedRecipe
        let updatedRecipeDTO = { Name = updatedRecipe.Name; NameAgain = updatedRecipe.Name; Json = updatedRecipeJson }
        let storeResult = storeUpdatedRecipe tableClient updatedRecipeDTO etag log
        match storeResult with
        | Ok _ ->
            let message = "Update of recipe '" + recipe.Name + "' with new link '" + newLink + "' was successful."
            log.LogInformation message
            Ok message
        | Error _ ->
            let error = "Update of recipe '" + recipeName + "' with new link '" + newLink + "' failed."
            log.LogWarning error
            Error error
    | Error _ ->
        let error = "Update recipe with new link failed."
        log.LogWarning error
        Error error

let updateRecipeWithNewBaseInfo (tableClient: CloudTableClient) (recipeName: RecipeName) (newRecipeName: RecipeName) (newPortions: Portions) (newLink: HttpLink) (log: ILogger) : Result<string, string> =
    let getResult = getRecipeForManipulation tableClient recipeName log
    match getResult with
    | Ok (recipe, etag) ->
        let updatedRecipe = { recipe with Name = newRecipeName; Portions = newPortions; Link = Some newLink }
        let updatedRecipeJson = Json.serialize updatedRecipe
        let updatedRecipeDTO = { Name = updatedRecipe.Name; NameAgain = updatedRecipe.Name; Json = updatedRecipeJson }
        let storeResult = storeUpdatedRecipe tableClient updatedRecipeDTO etag log
        match storeResult with
        | Ok _ ->
            let message = "Update of recipe '" + recipe.Name + "' with new name '" + updatedRecipe.Name + "', new portions '" + newPortions.ToString() + "', and new link '" + newLink + "' was successful."
            log.LogInformation message
            Ok message
        | Error _ ->
            let error = "Update of recipe '" + recipe.Name + "' with new name '" + updatedRecipe.Name + "', new portions '" + newPortions.ToString() + "', and new link '" + newLink + "' failed."
            log.LogWarning error
            Error error
    | Error _ ->
        let error = "Update recipe with new name, new portions and new link failed."
        log.LogWarning error
        Error error


/// Menus

type MenuDTO =
    {
        [<PartitionKey>] Name : MenuName
        [<RowKey>] NameAgain : MenuName
        Json : string
    }

let getMenusFromTable (tableClient: CloudTableClient) : MenuDTO list =
    let fromMenusTable q = fromTable tableClient "Menus" q
    let menus =
        Query.all<MenuDTO>
        |> fromMenusTable
        |> Seq.map (fun (b,_) -> b)
        |> Seq.toList
    menus

let findMenusUsingName (tableClient: CloudTableClient) (name: string) (log: ILogger) : MenuDTO list =
    let fromMenuTable q = fromTable tableClient "Menus" q
    log.LogInformation <| "Trying to find menus with Name: '" + name + "'."
    let menus =
        Query.all<MenuDTO>
        |> Query.where <@ fun _ s -> s.PartitionKey = name @>
        |> fromMenuTable
        |> Seq.map (fun (b,_) -> b)
        |> Seq.toList
    menus

let insertMenuInTable (tableClient: CloudTableClient) (menu: MenuDTO) (log: ILogger) : Result<string, string> =
    let inMenuTable r = inTable tableClient "Menus" r
    try
        let result = menu |> Insert |> inMenuTable
        match result.HttpStatusCode with
        | 200 | 201 | 202 | 203 | 204 | 205 ->
            let message = "Menu '" + menu.Name.ToString() + "' successfully inserted."
            log.LogInformation message
            Ok message
        | code ->
            let error = "Could not insert menu '" + menu.ToString() + "'.\nHTTP Status: '" + code.ToString() + "'."
            log.LogWarning error
            Error error
    with
        | :? StorageException as sx ->
            match sx.Message with
            | "Conflict" ->
                let error = "Insert failed due to conflicting Keys, a menu with name '" + menu.Name + "' already exists."
                log.LogWarning error
                Error error
            | _ ->
                let error = "Insert failed with exception:\n" + sx.ToString()
                log.LogWarning error
                Error error
        | ex ->
            let error = "Insert failed with exception:\n" + ex.ToString()
            log.LogWarning error
            Error error

let removeMenuFromTable (tableClient: CloudTableClient) (name: string) (log: ILogger) : Result<string, string> =
    let inMenuTable r = inTable tableClient "Menus" r
    try
        let result = { EntityIdentifier.PartitionKey = name; RowKey = name } |> ForceDelete |> inMenuTable
        match result.HttpStatusCode with
        | 200 | 201 | 202 | 203 | 204 | 205 ->
            let message = "Menu '" + name + "' successfully removed."
            log.LogInformation message
            Ok message
        | code ->
            let error = "Could not remove menu '" + name + "'.\nHTTP Status: '" + code.ToString() + "'."
            log.LogWarning error
            Error error
    with
        | :? StorageException as sx ->
            match sx.Message with
            | "Not Found" ->
                let error = "Remove failed due to that a menu with name: '" + name + "' could not be found."
                log.LogWarning error
                Error error
            | _ ->
                let error = "Remove failed with exception:\n" + sx.ToString()
                log.LogWarning error
                Error error
        | ex ->
            let error = "Remove failed with exception:\n" + ex.ToString()
            log.LogWarning error
            Error error

let getMenuForManipulation (tableClient: CloudTableClient) (menuName: MenuName) (log: ILogger) : Result<(Menu * string), string> =
    let fromMenuTable q = fromTable tableClient "Menus" q
    let inMenuTable r = inTable tableClient "Menus" r
    try
        let queryResults =
            Query.all<MenuDTO>
            |> Query.where <@ fun _ s -> s.PartitionKey = menuName && s.RowKey = menuName @>
            |> fromMenuTable
            |> Seq.toList
        match queryResults with
        | [] ->
            let error = "Get menu for manipulation failed since there is no menu with name: '" + menuName + "'."
            log.LogWarning error
            Error error
        | queryResults ->
            try
                let (menuDTO, metaData) = queryResults.Head
                let menu = Json.deserialize<Menu> menuDTO.Json
                let etag = metaData.Etag
                Ok (menu, etag)
            with
                | ex ->
                    let error = "Get menu for manipulation failed in deserialization, with exception:\n" + ex.ToString()
                    log.LogWarning error
                    Error error
    with
        | ex ->
            let error = "Get menu for manipulation failed in query, with exception:\n" + ex.ToString()
            log.LogWarning error
            Error error

let storeUpdatedMenu (tableClient: CloudTableClient) (menuDTO: MenuDTO) (etag: string) (log: ILogger) : Result<string, string> =
    let inMenuTable r = inTable tableClient "Menus" r
    try
        let replaceResult = (menuDTO, etag) |> Replace |> inMenuTable
        match replaceResult.HttpStatusCode with
        | 200 | 201 | 202 | 203 | 204 | 205 ->
            let message = "Store updated menu '" + menuDTO.Name + "' was successful."
            log.LogInformation message
            Ok message
        | code ->
            let error = "Store updated menu '" + menuDTO.Name + "' failed.\nHTTP Status: '" + code.ToString() + "'."
            log.LogWarning error
            Error error
    with
        | ex ->
            let error = "Store updated menu failed with exception:\n" + ex.ToString()
            log.LogWarning error
            Error error

let updateMenuWithNewName (tableClient: CloudTableClient) (menuName: MenuName) (newMenuName: MenuName) (log: ILogger) : Result<string, string> =
    let getResult = getMenuForManipulation tableClient menuName log
    match getResult with
    | Ok (menu, etag) ->
        let updatedMenu = { menu with Name = newMenuName } : Menu
        let updatedMenuJson = Json.serialize updatedMenu
        let updatedMenuDTO = { Name = updatedMenu.Name; NameAgain = updatedMenu.Name; Json = updatedMenuJson }
        let storeResult = storeUpdatedMenu tableClient updatedMenuDTO etag log
        match storeResult with
        | Ok _ ->
            let message = "Update of menu '" + menu.Name + "' with new name '" + updatedMenu.Name + "' was successful."
            log.LogInformation message
            Ok message
        | Error _ ->
            let error = "Update of menu '" + menuName + "' with new name '" + newMenuName + "' failed."
            log.LogWarning error
            Error error
    | Error _ ->
        let error = "Update menu with new name failed."
        log.LogWarning error
        Error error

let addItemToMenu (tableClient: CloudTableClient) (menuItem: MenuItem) (menuName: MenuName) (log: ILogger) : Result<string, string> =
    let getResult = getMenuForManipulation tableClient menuName log
    match getResult with
    | Ok (menu, etag) ->
        let menuItems = menu.Items
        let alreadyInList = List.exists (fun elem -> elem.RecipeName = menuItem.RecipeName && elem.WeekDay = menuItem.WeekDay) menuItems
        match alreadyInList with
        | false ->
            let updatedItems = menuItems @ [menuItem]
            let updatedMenu = { menu with Items = updatedItems }
            let updatedMenuJson = Json.serialize updatedMenu
            let updatedMenuDTO = { Name = updatedMenu.Name; NameAgain = updatedMenu.Name; Json = updatedMenuJson }
            let storeResult = storeUpdatedMenu tableClient updatedMenuDTO etag log
            match storeResult with
            | Ok _ ->
                let message = "Add menu item '" + menuItem.RecipeName + "' to menu '" + menu.Name + "' on: '" + menuItem.WeekDay.ToString() + "'was successful."
                log.LogInformation message
                Ok message
            | Error _ ->
                let error = "Add menu item '" + menuItem.RecipeName + "' to menu '" + menu.Name + "' on: '" + menuItem.WeekDay.ToString() + "' failed."
                log.LogWarning error
                Error error
        | true ->
            let error = "The same menu item already exists on the same day in the menu."
            log.LogWarning error
            Error error
    | Error _ ->            
        let error = "Add menu item to menu failed."
        log.LogWarning error
        Error error
 
let removeItemFromMenu (tableClient: CloudTableClient) (recipeName: RecipeName) (weekDay: WeekDay) (menuName: MenuName) (log: ILogger) : Result<string, string> =
    let getResult = getMenuForManipulation tableClient menuName log
    match getResult with
    | Ok (menu, etag) ->
        let menuItems = menu.Items
        let inList = List.exists (fun elem -> elem.RecipeName = recipeName && elem.WeekDay = weekDay) menuItems
        match inList with
        | true ->
            let updatedItems = List.filter (fun elem -> elem.RecipeName <> recipeName && elem.WeekDay <> weekDay) menuItems
            let updatedMenu = { menu with Items = updatedItems }
            let updatedMenuJson = Json.serialize updatedMenu
            let updatedMenuDTO = { Name = updatedMenu.Name; NameAgain = updatedMenu.Name; Json = updatedMenuJson }
            let storeResult = storeUpdatedMenu tableClient updatedMenuDTO etag log
            match storeResult with
            | Ok _ ->
                let message = "Remove menu item with recipe name '" + recipeName + "' on weekday '" + weekDay.ToString() + "' from menu '" + menu.Name + "' was successful."
                log.LogInformation message
                Ok message
            | Error _ ->
                let error = "Remove menu item with recipe name '" + recipeName + "' on weekday '" + weekDay.ToString() + "' from menu '" + menu.Name + "' failed."
                log.LogWarning error
                Error error
        | false ->
            let error = "The menu item could not be found in the menu."
            log.LogWarning error
            Error error
    | Error _ ->            
        let error = "Remove menu item from menu failed."
        log.LogWarning error
        Error error


/// ShoppingList

type ShoppingListDTO =
    {
        [<PartitionKey>] Name : ShoppingListName
        [<RowKey>] NameAgain : ShoppingListName
        Json : string
    }

let getShoppingListsFromTable (tableClient: CloudTableClient) : ShoppingListDTO list =
    let fromShoppingListsTable q = fromTable tableClient "ShoppingLists" q
    let shoppingLists =
        Query.all<ShoppingListDTO>
        |> fromShoppingListsTable
        |> Seq.map (fun (b,_) -> b)
        |> Seq.toList
    shoppingLists

let findShoppingListsUsingName (tableClient: CloudTableClient) (name: string) (log: ILogger) : ShoppingListDTO list =
    let fromShoppingListTable q = fromTable tableClient "ShoppingLists" q
    log.LogInformation <| "Trying to find shopping list with Name: '" + name + "'."
    let shoppingLists =
        Query.all<ShoppingListDTO>
        |> Query.where <@ fun _ s -> s.PartitionKey = name @>
        |> fromShoppingListTable
        |> Seq.map (fun (b,_) -> b)
        |> Seq.toList
    shoppingLists

let insertShoppingListInTable (tableClient: CloudTableClient) (shoppingList: ShoppingListDTO) (log: ILogger) : Result<string, string> =
    let inShoppingListTable r = inTable tableClient "ShoppingLists" r
    try
        let result = shoppingList |> Insert |> inShoppingListTable
        match result.HttpStatusCode with
        | 200 | 201 | 202 | 203 | 204 | 205 ->
            let message = "ShoppingList '" + shoppingList.Name.ToString() + "' successfully inserted."
            log.LogInformation message
            Ok message
        | code ->
            let error = "Could not insert shoppingList '" + shoppingList.ToString() + "'.\nHTTP Status: '" + code.ToString() + "'."
            log.LogWarning error
            Error error
    with
        | :? StorageException as sx ->
            match sx.Message with
            | "Conflict" ->
                let error = "Insert failed due to conflicting Keys, a shoppingList with name '" + shoppingList.Name + "' already exists."
                log.LogWarning error
                Error error
            | _ ->
                let error = "Insert failed with exception:\n" + sx.ToString()
                log.LogWarning error
                Error error
        | ex ->
            let error = "Insert failed with exception:\n" + ex.ToString()
            log.LogWarning error
            Error error

let removeShoppingListFromTable (tableClient: CloudTableClient) (name: string) (log: ILogger) : Result<string, string> =
    let inShoppingListTable r = inTable tableClient "ShoppingLists" r
    try
        let result = { EntityIdentifier.PartitionKey = name; RowKey = name } |> ForceDelete |> inShoppingListTable
        match result.HttpStatusCode with
        | 200 | 201 | 202 | 203 | 204 | 205 ->
            let message = "ShoppingList '" + name + "' successfully removed."
            log.LogInformation message
            Ok message
        | code ->
            let error = "Could not remove shoppingList '" + name + "'.\nHTTP Status: '" + code.ToString() + "'."
            log.LogWarning error
            Error error
    with
        | :? StorageException as sx ->
            match sx.Message with
            | "Not Found" ->
                let error = "Remove failed due to that a shoppingList with name: '" + name + "' could not be found."
                log.LogWarning error
                Error error
            | _ ->
                let error = "Remove failed with exception:\n" + sx.ToString()
                log.LogWarning error
                Error error
        | ex ->
            let error = "Remove failed with exception:\n" + ex.ToString()
            log.LogWarning error
            Error error

let getShoppingListForManipulation (tableClient: CloudTableClient) (shoppingListName: ShoppingListName) (log: ILogger) : Result<(ShoppingList * string), string> =
    let fromShoppingListTable q = fromTable tableClient "ShoppingLists" q
    let inShoppingListTable r = inTable tableClient "ShoppingLists" r
    try
        let queryResults =
            Query.all<ShoppingListDTO>
            |> Query.where <@ fun _ s -> s.PartitionKey = shoppingListName && s.RowKey = shoppingListName @>
            |> fromShoppingListTable
            |> Seq.toList
        match queryResults with
        | [] ->
            let error = "Get shoppingList for manipulation failed since there is no shoppingList with name: '" + shoppingListName + "'."
            log.LogWarning error
            Error error
        | queryResults ->
            try
                let (shoppingListDTO, metaData) = queryResults.Head
                let shoppingList = Json.deserialize<ShoppingList> shoppingListDTO.Json
                let etag = metaData.Etag
                Ok (shoppingList, etag)
            with
                | ex ->
                    let error = "Get shoppingList for manipulation failed in deserialization, with exception:\n" + ex.ToString()
                    log.LogWarning error
                    Error error
    with
        | ex ->
            let error = "Get shoppingList for manipulation failed in query, with exception:\n" + ex.ToString()
            log.LogWarning error
            Error error

let storeUpdatedShoppingList (tableClient: CloudTableClient) (shoppingListDTO: ShoppingListDTO) (etag: string) (log: ILogger) : Result<string, string> =
    let inShoppingListTable r = inTable tableClient "ShoppingLists" r
    try
        let replaceResult = (shoppingListDTO, etag) |> Replace |> inShoppingListTable
        match replaceResult.HttpStatusCode with
        | 200 | 201 | 202 | 203 | 204 | 205 ->
            let message = "Store updated shoppingList '" + shoppingListDTO.Name + "' was successful."
            log.LogInformation message
            Ok message
        | code ->
            let error = "Store updated shoppingList '" + shoppingListDTO.Name + "' failed.\nHTTP Status: '" + code.ToString() + "'."
            log.LogWarning error
            Error error
    with
        | ex ->
            let error = "Store updated shoppingList failed with exception:\n" + ex.ToString()
            log.LogWarning error
            Error error

let updateShoppingListWithNewName (tableClient: CloudTableClient) (shoppingListName: ShoppingListName) (newShoppingListName: ShoppingListName) (log: ILogger) : Result<string, string> =
    let getResult = getShoppingListForManipulation tableClient shoppingListName log
    match getResult with
    | Ok (shoppingList, etag) ->
        let updatedShoppingList = { shoppingList with Name = newShoppingListName } : ShoppingList
        let updatedShoppingListJson = Json.serialize updatedShoppingList
        let updatedShoppingListDTO = { Name = updatedShoppingList.Name; NameAgain = updatedShoppingList.Name; Json = updatedShoppingListJson }
        let storeResult = storeUpdatedShoppingList tableClient updatedShoppingListDTO etag log
        match storeResult with
        | Ok _ ->
            let message = "Update of shoppingList '" + shoppingList.Name + "' with new name '" + updatedShoppingList.Name + "' was successful."
            log.LogInformation message
            Ok message
        | Error _ ->
            let error = "Update of shoppingList '" + shoppingListName + "' with new name '" + newShoppingListName + "' failed."
            log.LogWarning error
            Error error
    | Error _ ->
        let error = "Update shoppingList with new name failed."
        log.LogWarning error
        Error error

let addItemToShoppingList (tableClient: CloudTableClient) (shoppingItem: ShoppingItem) (shoppingListName: ShoppingListName) (log: ILogger) : Result<string, string> =
    let getResult = getShoppingListForManipulation tableClient shoppingListName log
    match getResult with
    | Ok (shoppingList, etag) ->
        let shoppingItems = shoppingList.Items
        let alreadyInList = List.exists (fun (elem : ShoppingItem) -> elem.Name = shoppingItem.Name) shoppingItems
        match alreadyInList with
        | false ->
            let updatedItems = shoppingItems @ [shoppingItem]
            let updatedShoppingList = { shoppingList with Items = updatedItems }
            let updatedShoppingListJson = Json.serialize updatedShoppingList
            let updatedShoppingListDTO = { Name = updatedShoppingList.Name; NameAgain = updatedShoppingList.Name; Json = updatedShoppingListJson }
            let storeResult = storeUpdatedShoppingList tableClient updatedShoppingListDTO etag log
            match storeResult with
            | Ok _ ->
                let message = "Add shoppingList item '" + shoppingItem.Name + "' to shoppingList '" + shoppingList.Name + "'was successful."
                log.LogInformation message
                Ok message
            | Error _ ->
                let error = "Add shoppingList item '" + shoppingItem.Name + "' to shoppingList '" + shoppingList.Name + "' failed."
                log.LogWarning error
                Error error
        | true ->
            let error = "The same shoppingList item already exists on the same day in the shoppingList."
            log.LogWarning error
            Error error
    | Error _ ->            
        let error = "Add shoppingList item to shoppingList failed."
        log.LogWarning error
        Error error
 
let removeItemFromShoppingList (tableClient: CloudTableClient) (shoppingItemName: ShoppingItemName) (shoppingListName: ShoppingListName) (log: ILogger) : Result<string, string> =
    let getResult = getShoppingListForManipulation tableClient shoppingListName log
    match getResult with
    | Ok (shoppingList, etag) ->
        let shoppingItems = shoppingList.Items
        let inList = List.exists (fun (elem : ShoppingItem) -> elem.Name = shoppingItemName) shoppingItems
        match inList with
        | true ->
            let updatedItems = List.filter (fun (elem : ShoppingItem) -> elem.Name <> shoppingItemName) shoppingItems
            let updatedShoppingList = { shoppingList with Items = updatedItems }
            let updatedShoppingListJson = Json.serialize updatedShoppingList
            let updatedShoppingListDTO = { Name = updatedShoppingList.Name; NameAgain = updatedShoppingList.Name; Json = updatedShoppingListJson }
            let storeResult = storeUpdatedShoppingList tableClient updatedShoppingListDTO etag log
            match storeResult with
            | Ok _ ->
                let message = "Remove shoppingList item with recipe name '" + shoppingItemName + "' from shoppingList '" + shoppingList.Name + "' was successful."
                log.LogInformation message
                Ok message
            | Error _ ->
                let error = "Remove shoppingList item with recipe name '" + shoppingItemName + "' from shoppingList '" + shoppingList.Name + "' failed."
                log.LogWarning error
                Error error
        | false ->
            let error = "The shoppingList item could not be found in the shoppingList."
            log.LogWarning error
            Error error
    | Error _ ->            
        let error = "Remove shoppingList item from shoppingList failed."
        log.LogWarning error
        Error error

