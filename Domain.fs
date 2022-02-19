module WebBackend.Domain

open FSharp.Azure.Storage.Table

// Books

type Book =
    {
        [<PartitionKey>] Author: string
        [<RowKey>] Title: string
        IsFavorite: bool
    }


// Meals

type ProductName = string
type ShoppingItemName = string
type ShoppingListName = string
type RecipeName = string
type MenuName = string

type RecipeUnit =
    | Piece
    | Teaspoon
    | Tablespoon
    | Deciliter
    | Liter
    | Gram
    | Hectogram
    | Kilogram
    | NotDefined

type Quantity =
    {
        Amount : float
        Unit : RecipeUnit
    }

type SearchString = string

type HttpLink = string

type Comment =
    {
        Comment : string
    }

type Instruction =
    {
        Instruction : string
    }

type Portions = int

type RecipeInstruction = Instruction

type ShoppingInstruction = Instruction

type WeekDay =
    | Monday
    | Tuesday
    | Wednesday
    | Thursday
    | Friday
    | Saturday
    | Sunday

type Product =
    {
        Name : ProductName
        Link : HttpLink option
        Comments : Comment list
    }

type QuantifiedProduct =
    {
        ProductName : ProductName
        Quantity : Quantity
    }

type Ingredient = QuantifiedProduct

type ShoppingItem =
    {
        Name : ShoppingItemName
        Item : QuantifiedProduct
        Comment : ShoppingInstruction
    }

type Recipe =
    {
        Name : RecipeName
        Link : HttpLink option
        Portions : Portions
        Ingredients : Ingredient list
        Instructions : Instruction list
        TastingNotes : Comment list
    }

type RecipeDTO =
    {
        [<PartitionKey>] Name : RecipeName
        [<RowKey>] Json : string
    }

type ShoppingList =
    {
        Name : ShoppingListName
        Items : ShoppingItem list
    }

type MenuItem =
    {
        Recipe : Recipe
        WeekDay : WeekDay
    }

type Menu =
    {
        Name : MenuName
        Items : MenuItem list
    }


// Function types

type RecalculatePortions = Recipe -> Portions -> Recipe
type AddTastingNote = Recipe -> Comment -> Recipe
type ListRecipes = unit -> Recipe list
type ListMenus = unit -> Menu list
type ListShoppingLists = unit -> ShoppingList list
type SearchRecipe = SearchString -> RecipeName list
type SearchMenu = SearchString -> MenuName list
type SearchShoppingList = SearchString -> ShoppingListName list
type GetRecipe = RecipeName -> Recipe
type GetMenu = MenuName -> Menu
type GetShoppingList = ShoppingListName -> ShoppingList
type RemoveRecipe = Recipe -> unit
type RemoveMenu = Menu -> unit
type RemoveShoppingList = ShoppingList -> unit
type StoreRecipe = Recipe -> unit
type StoreMenu = Menu -> unit
type StoreShoppingList = ShoppingList -> unit
