module WebBackend.HttpJsonController

open Microsoft.Extensions.Logging

open FSharp.Json

open Domain

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
