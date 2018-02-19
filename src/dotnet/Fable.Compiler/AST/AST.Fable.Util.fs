module Fable.AST.Fable.Util

open Fable
open Fable.AST

// let (|CoreMeth|_|) coreMod meth expr =
//     match expr with
//     | Call(Callee(Import(meth',coreMod',CoreLib,_)),None,args,_,_,_)
//         when meth' = meth && coreMod' = coreMod ->
//         Some args
//     | _ -> None

let addWarning (com: ICompiler) (fileName: string) (range: SourceLocation option) (warning: string) =
    com.AddLog(warning, Severity.Warning, ?range=range, fileName=fileName)

let addError (com: ICompiler) (fileName: string) (range: SourceLocation option) (warning: string) =
    com.AddLog(warning, Severity.Error, ?range=range, fileName=fileName)

let addErrorAndReturnNull (com: ICompiler) (fileName: string) (range: SourceLocation option) (error: string) =
    com.AddLog(error, Severity.Error, ?range=range, fileName=fileName)
    Null Any |> Value

/// When referenced multiple times, is there a risk of double evaluation?
let hasDoubleEvalRisk = function
    | IdentExpr _
    // TODO: Add Union and List Getters here?
    | Value(This _ | Null _ | UnitConstant | NumberConstant _
                | StringConstant _ | BoolConstant _ | Enum _) -> false
    | Get(_,kind,_,_) ->
        match kind with
        // OptionValue has a runtime check
        | ListHead | ListTail | TupleGet _
        | UnionTag _ | UnionField _ -> false
        | _ -> true
    | _ -> true

let attachRange (range: SourceLocation option) msg =
    match range with
    | Some range -> msg + " " + (string range)
    | None -> msg

let attachRangeAndFile (range: SourceLocation option) (fileName: string) msg =
    match range with
    | Some range -> msg + " " + (string range) + " (" + fileName + ")"
    | None -> msg + " (" + fileName + ")"

type OperationKind =
    | InstanceCall of callee: Expr * meth: string * args: Expr list
    | ImportCall of importPath: string * modName: string * meth: string option * isCons: bool * args: Expr list
    | CoreLibCall of modName: string * meth: string option * isCons: bool * args: Expr list
    | GlobalCall of modName: string * meth: string option * isCons: bool * args: Expr list

let makeIdent name =
    { Name = name
      Type = Any
      IsMutable = false
      Range = None }

let makeTypedIdent typ name =
    { makeIdent name with Type = typ }

let makeLoop range loopKind = Loop (loopKind, range)

let makeCoreRef t modname prop =
    Import(prop, modname, CoreLib, t)

let makeImport t (selector: string) (path: string) =
    Import(selector.Trim(), path.Trim(), CustomImport, t)

let makeBinOp range typ left right op =
    Operation(BinaryOperation(op, left, right), typ, range)

let makeUnOp range typ arg op =
    Operation(UnaryOperation(op, arg), typ, range)

let makeLogOp range left right op =
    Operation(LogicalOperation(op, left, right), Boolean, range)

let makeEqOp range left right op =
    Operation(BinaryOperation(op, left, right), Boolean, range)

let makeIndexGet range typ callee idx =
    Get(callee, IndexGet idx, typ, range)

let makeFieldGet range typ callee field =
    Get(callee, FieldGet field, typ, range)

let makeUntypedFieldGet callee field =
    Get(callee, FieldGet field, Any, None)

let makeArray elementType arrExprs =
    NewArray(ArrayValues arrExprs, elementType) |> Value

let makeCall r t (applied: Expr) args =
    let callInfo =
        { argTypes =
            match applied.Type with
            | Fable.FunctionType(Fable.LambdaType arg, _) -> [arg]
            | Fable.FunctionType(Fable.DelegateType args, _) -> args
            | _ -> [Any]
          isConstructor = false
          hasSpread = false
          hasThisArg = false }
    Operation(Call(applied, None, args, callInfo), t, r)

let makeLongInt (x: uint64) unsigned =
    let t = ExtendedNumber(if unsigned then UInt64 else Int64)
    let lowBits = NumberConstant (float (uint32 x), Float64)
    let highBits = NumberConstant (float (x >>> 32), Float64)
    let unsigned = BoolConstant (unsigned)
    let args = [Value lowBits; Value highBits; Value unsigned]
    makeCall None t (makeCoreRef Any "Long" "fromBits") args

let makeBoolConst (x: bool) = BoolConstant x |> Value
let makeStrConst (x: string) = StringConstant x |> Value
let makeIntConst (x: int) = NumberConstant (float x, Int32) |> Value
let makeNumConst (x: float) = NumberConstant (float x, Float64) |> Value
let makeDecConst (x: decimal) = NumberConstant (float x, Float64) |> Value

let makeFloat32 (x: float32) =
    let args = [NumberConstant (float x, Float32) |> Value]
    let callee = makeUntypedFieldGet (IdentExpr(makeIdent "Math")) "fround"
    makeCall None (Number Float32) callee args

let makeTypeConst (typ: Type) (value: obj) =
    match typ, value with
    // Long Integer types
    | ExtendedNumber Int64, (:? int64 as x) -> makeLongInt (uint64 x) false
    | ExtendedNumber UInt64, (:? uint64 as x) -> makeLongInt x true
    // Decimal type
    | ExtendedNumber Decimal, (:? decimal as x) -> makeDecConst x
    // Short Float type
    | Number Float32, (:? float32 as x) -> makeFloat32 x
    | Boolean, (:? bool as x) -> BoolConstant x |> Value
    | String, (:? string as x) -> StringConstant x |> Value
    | Char, (:? char as x) -> StringConstant (string x) |> Value
    // Integer types
    | Number UInt8, (:? byte as x) -> NumberConstant (float x, UInt8) |> Value
    | Number Int8, (:? sbyte as x) -> NumberConstant (float x, Int8) |> Value
    | Number Int16, (:? int16 as x) -> NumberConstant (float x, Int16) |> Value
    | Number UInt16, (:? uint16 as x) -> NumberConstant (float x, UInt16) |> Value
    | Number Int32, (:? int as x) -> NumberConstant (float x, Int32) |> Value
    | Number UInt32, (:? uint32 as x) -> NumberConstant (float x, UInt32) |> Value
    // Float types
    | Number Float64, (:? float as x) -> NumberConstant (float x, Float64) |> Value
    // Enums
    | EnumType _, (:? int64)
    | EnumType _, (:? uint64) -> failwith "int64 enums are not supported"
    | EnumType(_, name), (:? byte as x) -> Enum(NumberEnum(int x), name) |> Value
    | EnumType(_, name), (:? sbyte as x) -> Enum(NumberEnum(int x), name) |> Value
    | EnumType(_, name), (:? int16 as x) -> Enum(NumberEnum(int x), name) |> Value
    | EnumType(_, name), (:? uint16 as x) -> Enum(NumberEnum(int x), name) |> Value
    | EnumType(_, name), (:? int as x) -> Enum(NumberEnum(int x), name) |> Value
    | EnumType(_, name), (:? uint32 as x) -> Enum(NumberEnum(int x), name) |> Value
    // TODO: Regex
    | Unit, _ -> UnitConstant |> Value
    // Arrays with small data type (ushort, byte) are represented
    // in F# AST as BasicPatterns.Const
    | Array (Number kind), (:? (byte[]) as arr) ->
        let values = arr |> Array.map (fun x -> NumberConstant (float x, kind) |> Value) |> Seq.toList
        NewArray (ArrayValues values, Number kind) |> Value
    | Array (Number kind), (:? (uint16[]) as arr) ->
        let values = arr |> Array.map (fun x -> NumberConstant (float x, kind) |> Value) |> Seq.toList
        NewArray (ArrayValues values, Number kind) |> Value
    | _ -> failwithf "Unexpected type %A, literal %O" typ value

// let makeJsObject range (props: (string * Expr) list) =
//     let membs = props |> List.map (fun (name, body) ->
//         let m = Member.Create(name, Field, [], body.Type)
//         m, [], body)
//     ObjectExpr(membs, range)

let getTypedArrayName (com: ICompiler) numberKind =
    match numberKind with
    | Int8 -> "Int8Array"
    | UInt8 -> if com.Options.clampByteArrays then "Uint8ClampedArray" else "Uint8Array"
    | Int16 -> "Int16Array"
    | UInt16 -> "Uint16Array"
    | Int32 -> "Int32Array"
    | UInt32 -> "Uint32Array"
    | Float32 -> "Float32Array"
    | Float64 -> "Float64Array"

// let makeCall (range: SourceLocation option) typ kind =
//     let call meth isCons args callee =
//         // TODO: Parameterize hasSpread
//         Operation(Call(callee, meth, args, isCons, hasSpread=false), typ, range)
//     match kind with
//     | InstanceCall (callee, meth, args) ->
//         call (Some meth) false args callee
//     | ImportCall (importPath, modName, meth, isCons, args) ->
//         Import (modName, importPath, CustomImport, Any) |> call meth isCons args
//     | CoreLibCall (modName, meth, isCons, args) ->
//         makeCoreRef Any modName (defaultArg meth "default") |> call None isCons args
//     | GlobalCall (modName, meth, isCons, args) ->
//         call meth isCons args (makeIdentExpr modName)

// let isUncurried = function
//     // Arguments coming from the outside should be already uncurried
//     | IdentExpr (ident, _) -> ident.IsUncurried
//     // Assume functions in (usually record) fields are already uncurried
//     | Get _ -> true // TODO: Check it's actually a record? (We'd have to edit the AST)
//     | _ -> false

// let getArity typ =
//     let rec getArityInner acc = function
//         | LambdaType(_, retType) -> getArityInner (acc + 1) retType
//         | _ -> acc
//     getArityInner 0 typ

// /// Actually uncurries an expression that had been marked for that purpose
// /// ATTENTION: This must be called only right before transforming the expression to JS
// let uncurry arity expr =
//     let rec (|UncurriedLambda|_|) arity expr =
//         let rec uncurryLambda r accArgs remainingArity expr =
//             if remainingArity = Some 0
//             then Lambda(List.rev accArgs, expr, r) |> Some
//             else
//                 match expr, remainingArity with
//                 | Lambda(args, body, r2), _ ->
//                     let remainingArity = remainingArity |> Option.map (fun x -> x - 1)
//                     uncurryLambda (Option.orElse r2 r) (args@accArgs) remainingArity body
//                 // If there's no arity expectation we can return the flattened part
//                 | _, None when List.isEmpty accArgs |> not ->
//                     Lambda(List.rev accArgs, expr, r) |> Some
//                 // We cannot flatten lambda to the expected arity
//                 | _, _ -> None
//         uncurryLambda None [] arity expr
//     if isUncurried expr then
//         expr // TODO: Check edge cases (expr has more than expected arity)
//     else
//         match expr, arity with
//         | UncurriedLambda arity lambda, _ -> lambda
//         | _, Some arity ->
//             CoreLibCall("Util", Some "uncurry", false, [makeIntConst arity; expr])
//             |> makeCall None expr.Type
//         | _, None -> expr

// /// Checks if the applied expression is uncurried
// /// Returns transformed (or not) applied, arg expressions and
// /// ATTENTION: This must be called only right before transforming the expression to JS
// let applyCurried applied args =
//     let rec flattenApplication accArgs = function
//         | Apply(applied, args, _) ->
//              flattenApplication (args::accArgs) applied
//         | applied -> applied, List.rev accArgs
//     let innerApplied, flattenedArgs = flattenApplication [args] applied
//     if isUncurried innerApplied then
//         match getArity innerApplied.Type, List.length flattenedArgs with
//         | arity, argsLength when argsLength < arity ->

//             failwith "TODO"
//             // let args = [makeIntConst (arity - argsLength); innerApplied; makeArray Any (List.concat flattenedArgs)]
//             // CoreLibCall("Util", Some "partial", false, args) |> makeCall r Any
//         | _ ->
//             innerApplied, (List.concat flattenedArgs), None // TODO: Remove single unit argument
//     else
//         applied, args, None // TODO: Remove single unit argument

// let private markUncurriedArgs argTypes (argExprs: Expr list) =
//     let rec markUncurry accArgs = function
//         | argType::restTypes, argExpr::restExprs ->
//             let argExpr =
//                 match getArity argType with
//                 | 0 | 1 -> argExpr
//                 | arity -> Uncurry(argExpr, Some arity)
//             markUncurry (argExpr::accArgs) (restTypes, restExprs)
//         | [], argExprs ->
//             (List.rev accArgs) @ argExprs
//         | _, [] ->
//             List.rev accArgs
//     match argTypes with
//     | Some argTypes -> markUncurry [] (argTypes, argExprs)
//     | None -> argExprs |> List.map (fun e ->
//         match e.Type with
//         | LambdaType _ -> Uncurry(e, None)
//         | _ -> e)

// // TODO: Remove single unit argument
// let private removeOptionalArguments (optionalArgs: int) (args: Expr list) =
//     let rec removeArgs optionalArgs (revArgs: Expr list) =
//         match revArgs with
//         | Value(NoneConst _)::rest when optionalArgs > 0 ->
//             removeArgs (optionalArgs - 1) rest
//         | _ -> args
//     List.rev args |> removeArgs optionalArgs |> List.rev

// type CallHelper =
//     /// Uncurry functions passed as arguments and other operations
//     static member PrepareArgs(argExprs: Expr list,
//                               ?argTypes: Type list,
//                               ?hasRestParams: bool,
//                               ?optionalArgs: int,
//                               ?untupleArgs: bool) =
//         let argExprs =
//             match hasRestParams, untupleArgs, optionalArgs, argExprs with
//             | Some true, _, _, (_::_) ->
//                 let argExprs = List.rev argExprs
//                 match argExprs.Head with
//                 | Value(NewArray(items, _)) -> (List.rev argExprs.Tail)@items
//                 | _ -> (Spread argExprs.Head)::argExprs.Tail |> List.rev
//             // TODO: hasSeqParam
//             // TODO: If we're within a constructor and call to another constructor, pass `$this` as last argument
//             | _, Some true, _, [Value(NewTuple argExprs)] ->
//                 argExprs
//             | _, _, Some optionalArgs, _ when optionalArgs > 0 ->
//                 removeOptionalArguments optionalArgs argExprs // See #231, #640
//             | _ ->
//                 argExprs
//         // TODO: We shouldn't uncurry args for setters
//         markUncurriedArgs argTypes argExprs

let rec makeTypeTest com fileName range expr (typ: Type) =
    let jsTypeof (primitiveType: string) expr =
        let typof = makeUnOp None String expr UnaryTypeof
        makeEqOp range typof (makeStrConst primitiveType) BinaryEqualStrict
    let jsInstanceof (cons: Expr) expr =
        makeBinOp range Boolean expr cons BinaryInstanceOf
    match typ with
    | Any -> makeBoolConst true
    | Unit -> makeEqOp range expr (Value <| Null Any) BinaryEqual
    | Boolean -> jsTypeof "boolean" expr
    | Char | String _ -> jsTypeof "string" expr
    | Regex -> jsInstanceof (IdentExpr(makeIdent "RegExp")) expr
    | Number _ | EnumType _ -> jsTypeof "number" expr
    | ExtendedNumber (Int64|UInt64) -> jsInstanceof(makeCoreRef Any "Long" "default") expr
    | ExtendedNumber Decimal -> jsTypeof "number" expr
    | ExtendedNumber BigInt -> jsInstanceof (makeCoreRef Any "BigInt" "default") expr
    | FunctionType _ -> jsTypeof "function" expr
    | Array _ | Tuple _ | List _ ->
        makeCall None Boolean (makeCoreRef Any "Util" "isArray") [expr]
    | DeclaredType (ent, _) ->
        failwith "TODO: DeclaredType type test"
        // if ent.IsClass
        // then jsInstanceof (TypeRef ent) expr
        // else "Cannot type test interfaces, records or unions"
        //      |> addErrorAndReturnNull com fileName range
    | Option _ | GenericParam _ | ErasedUnion _ ->
        "Cannot type test options, generic parameters or erased unions"
        |> addErrorAndReturnNull com fileName range

// /// Helper when we need to compare the types of the arguments applied to a method
// /// (concrete) with the declared argument types for that method (may be generic)
// /// (e.g. when resolving a TraitCall)
// let compareDeclaredAndAppliedArgs declaredArgs appliedArgs =
//     // Curried functions returning a generic can match a function
//     // with higher arity (see #1262)
//     let rec funcTypesEqual eq types1 types2 =
//         match types1, types2 with
//         | [], [] -> true
//         | [GenericParam _], _ -> true
//         | head1::rest1, head2::rest2 ->
//             eq head1 head2 && funcTypesEqual eq rest1 rest2
//         | _ -> false
//     let listsEqual eq li1 li2 =
//         if not(List.sameLength li1 li2)
//         then false
//         else List.fold2 (fun b x y -> if b then eq x y else false) true li1 li2
//     let rec argEqual x y =
//         match x, y with
//         | Option genArg1, Option genArg2
//         | Array genArg1, Array genArg2 ->
//             argEqual genArg1 genArg2
//         | Tuple genArgs1, Tuple genArgs2 ->
//             listsEqual argEqual genArgs1 genArgs2
//         | LambdaType (genArgs1, returnType1), LambdaType (genArgs2, returnType2) ->
//             match genArgs1, genArgs2 with
//             | [], [] -> argEqual returnType1 returnType2
//             | head1::rest1, head2::rest2 ->
//                 if argEqual head1 head2 then
//                     let types1 = rest1@[returnType1]
//                     let types2 = rest2@[returnType2]
//                     funcTypesEqual argEqual types1 types2
//                 else false
//             | _ -> false
//         | DeclaredType(ent1, genArgs1), DeclaredType(ent2, genArgs2) ->
//             ent1 = ent2 && listsEqual argEqual genArgs1 genArgs2
//         | GenericParam _, _ ->
//             true
//         | x, y -> x = y
//     listsEqual argEqual declaredArgs appliedArgs