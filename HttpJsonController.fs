module WebBackend.HttpJsonController

open Microsoft.Extensions.Logging

open FSharp.Json

open Domain

type Action
    = Unknown
    | FindRecipe
    | InsertRecipe
    | RemoveRecipe
    | ChangeRecipeName
    | UpdateRecipeLink
    | ChangeRecipePortions
    | AddIngredientToRecipe
    | AddInstructionToRecipe
    | AddCommentToRecipe
    | RemoveIngredientFromRecipe
    | RemoveInstructionFromRecipe
    | RemoveCommentFromRecipe

type CommandJson =
    {
        Action: Action option
    }

type RecipeNameJson =
    {
        RecipeName: string option
    }

type NewRecipeNameJson =
    {
        NewRecipeName: string option
    }

type IngredientNameJson =
    {
        IngredientName: string option
    }

type PortionsJson =
    {
        Portions: int option
    }

type LinkJson =
    {
        Link: HttpLink option
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

let getRecipeNameFromReqBody (body: string) (log: ILogger) : RecipeNameJson =
    try
        let recipeName = Json.deserialize<RecipeNameJson> body
        recipeName
    with
        ex ->
            log.LogWarning <| "Get Recipe name failed with exception:\n" + ex.ToString()
            { RecipeName = None }

let getNewRecipeNameFromReqBody (body: string) (log: ILogger) : NewRecipeNameJson =
    try
        let newRecipeName = Json.deserialize<NewRecipeNameJson> body
        newRecipeName
    with
        ex ->
            log.LogWarning <| "Get NewRecipe name failed with exception:\n" + ex.ToString()
            { NewRecipeName = None }

let getRecipeFromReqBody (body: string) (log: ILogger) : Recipe =
    try
        let recipe = Json.deserialize<Recipe> body
        recipe
    with
        ex ->
            log.LogWarning <| "Get Recipe failed with exception:\n" + ex.ToString()
            { Name = ""; Link = None; Portions = 0; Ingredients = []; Instructions = []; Comments = [] }

let getProductFromReqBody (body: string) (log: ILogger) : Product =
    try
        let product = Json.deserialize<Product> body
        product
    with
        ex ->
            log.LogWarning <| "Get Product failed with exception:\n" + ex.ToString()
            { Name = ""; Link = None; Comments = [] }

let getIngredientNameFromReqBody (body: string) (log: ILogger) : IngredientNameJson =
    try
        let ingredientName = Json.deserialize<IngredientNameJson> body
        ingredientName
    with
        ex ->
            log.LogWarning <| "Get Ingredient name failed with exception:\n" + ex.ToString()
            { IngredientName = None }

let getIngredientFromReqBody (body: string) (log: ILogger) : Ingredient =
    try
        let ingredient = Json.deserialize<Ingredient> body
        ingredient
    with
        ex ->
            log.LogWarning <| "Get Ingredient failed with exception:\n" + ex.ToString()
            { Product = { Name = ""; Link = None; Comments = [] }; Quantity = { Amount = 0.0; Unit = NotDefined } }

let getInstructionFromReqBody (body: string) (log: ILogger) : Instruction =
    try
        let instruction = Json.deserialize<Instruction> body
        instruction
    with
        ex ->
            log.LogWarning <| "Get Instruction failed with exception:\n" + ex.ToString()
            { Instruction = "" }

let getCommentFromReqBody (body: string) (log: ILogger) : Comment =
    try
        let comment = Json.deserialize<Comment> body
        comment
    with
        ex ->
            log.LogWarning <| "Get Instruction failed with exception:\n" + ex.ToString()
            { Comment = "" }

let getPortionsFromReqBody (body: string) (log: ILogger) : PortionsJson =
    try
        let portions = Json.deserialize<PortionsJson> body
        portions
    with
        ex ->
            log.LogWarning <| "Get Portions failed with exception:\n" + ex.ToString()
            { Portions = None }

let getLinkFromReqBody (body: string) (log: ILogger) : LinkJson =
    try
        let link = Json.deserialize<LinkJson> body
        link
    with
        ex ->
            log.LogWarning <| "Get Portions failed with exception:\n" + ex.ToString()
            { Link = None }

