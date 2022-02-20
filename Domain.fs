module WebBackend.Domain


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
        Product : Product
        Quantity : Quantity
    }

type Ingredient = QuantifiedProduct

type ShoppingItem =
    {
        Name : ShoppingItemName
        Item : QuantifiedProduct
        Comment : Comment list
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
