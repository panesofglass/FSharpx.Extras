namespace FSharpx

open System
open System.Collections
open System.Collections.Generic
#if NET40
open System.Diagnostics.Contracts
#else
open System.Diagnostics
#endif
open System.Runtime.CompilerServices
            
module Enumerator = 
 
    let singleton a = 
        let state = ref 0
        { new IEnumerator<_> with 
            member self.Current = if (state.Value = 1) then a else invalidOp "!"
          interface System.Collections.IEnumerator with
            member self.Current = if (state.Value = 1) then box a else invalidOp "!"
            member self.MoveNext() = state := state.Value + 1; state.Value = 1
            member self.Reset() = state := 0
          interface System.IDisposable with 
            member self.Dispose() = () }
  
    let bind (f:_ -> IEnumerator<'b>) (a:IEnumerator<'a>) =
        if a.MoveNext() then f (Some(a.Current)) else f(None) 
   
    let combine (a:IEnumerator<_>) b = 
        let current = ref a
        let first = ref true
        { new IEnumerator<_> with 
            member self.Current = current.Value.Current
          interface System.Collections.IEnumerator with
            member self.Current = box current.Value.Current
            member self.MoveNext() = 
                if current.Value.MoveNext() then true 
                elif first.Value then 
                    current := b
                    first := false
                    current.Value.MoveNext()
                else false
            member self.Reset() = 
                a.Reset(); b.Reset()
                first := true; current := a
          interface System.IDisposable with 
            member self.Dispose() = a.Dispose(); b.Dispose() }
    
    let empty<'a> : IEnumerator<'a> = 
        { new IEnumerator<_> with 
            member self.Current = invalidOp "!"
          interface System.Collections.IEnumerator with
            member self.Current = invalidOp "!"
            member self.MoveNext() = false
            member self.Reset() = ()
          interface System.IDisposable with 
            member self.Dispose() = ()}
    
    let delay f = 
        let en : Lazy<IEnumerator<_>> = lazy f()
        { new IEnumerator<_> with 
            member self.Current = en.Value.Current
          interface System.Collections.IEnumerator with
            member self.Current = box en.Value.Current
            member self.MoveNext() = en.Value.MoveNext()
            member self.Reset() = en.Value.Reset()
          interface System.IDisposable with 
            member self.Dispose() = en.Value.Dispose() }
   
    let toSeq gen = 
        { new IEnumerable<'T> with 
              member x.GetEnumerator() = gen()
          interface IEnumerable with 
              member x.GetEnumerator() = (gen() :> IEnumerator) }

    type EnumerableBuilder() = 
        member x.Delay(f) = delay f
        member x.YieldFrom(a) = a
        member x.Yield(a) = singleton a
        member x.Bind(a:IEnumerator<'a>, f:_ -> IEnumerator<'b>) = bind f a
        member x.Combine(a, b) = combine a b
        member x.Zero() = empty<_>
    let iter = new EnumerableBuilder()    

    let head (en:IEnumerator<'a>) =
        if en.MoveNext() then en.Current
        else invalidOp "!"

    let firstOrDefault def (en:IEnumerator<'a>) =
        if en.MoveNext() then en.Current
        else def

    let rec lastOrDefault def (en:IEnumerator<'a>) =
        if en.MoveNext() then
            lastOrDefault en.Current en
        else def

    let last (en:IEnumerator<'a>) =
        if en.MoveNext() then
            lastOrDefault en.Current en
        else invalidOp "!"

    let length (en:IEnumerator<'a>) =
        let rec loop acc =
            if en.MoveNext() then loop (acc+1)
            else acc
        loop 0
  
    let skip n (en:IEnumerator<'a>) =
        if n = 0 then en
        else
            let rec loop acc =
                if not (en.MoveNext()) then empty<_>
                elif n = acc then en
                else loop (acc+1)
            loop 1
  
    let take n (en:IEnumerator<'a>) =
        if n = 0 then empty<_>
        else
            let rec loop acc = iter {
                let! v = en
                match v with
                | None -> ()
                | Some v' ->
                    yield v'
                    if acc = n then ()
                    else yield! loop (acc+1) }
            loop 1

    // Implementing the zip function for enumerators
    let rec zip xs ys = iter {
        let! x = xs
        let! y = ys
        match x, y with 
        | Some(x), Some(y) ->
            yield x, y
            yield! zip xs ys 
        | _ -> () }
   
    let rec map f en = iter {
        let! x = en
        match x with 
        | Some(x) -> yield f x
                     yield! map f en
        | _ -> () }               

    /// Scan progressively folds over the enumerator, returning a new enumerator
    /// that lazily computes each state.
    let rec scan f state (en : IEnumerator<_>) = iter {
        yield state
        if en.MoveNext() then
            let state' = f state en.Current
            yield! scan f state' en }

    let private scanWithPredicate f state pred (en : IEnumerator<_>) = iter {
        yield state
        if en.MoveNext() then
            let state' = f state en.Current
            if pred state' then
                yield! scan f state' en }

    /// Scan progressively folds over the enumerator, returning a new enumerator
    /// that lazily computes each state while the provided predicate is true.
    let scanWhile f state pred en = scanWithPredicate f state pred en

    /// Scan progressively folds over the enumerator, returning a new enumerator
    /// that lazily computes each state until the provided predicate is true.
    let scanUntil f state pred en = scanWithPredicate f state (not << pred) en
   
module Seq =
    /// <summary>
    /// Adds an index to a sequence
    /// </summary>
    /// <param name="a"></param>
    let inline index a = Seq.mapi tuple2 a

    /// <summary>
    /// Returns the first element (with its index) for which the given function returns true.
    /// Return None if no such element exists.
    /// </summary>
    /// <param name="pred">Predicate</param>
    /// <param name="l">Sequence</param>
    let tryFindWithIndex pred l =
        l |> index |> Seq.tryFind (fun (_,v) -> pred v)

    let inline lift2 f l1 l2 = 
        seq {
            for i in l1 do
                for j in l2 do
                    yield f i j }
        

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Array = 
    let inline nth i arr = Array.get arr i
    let inline setAt i v arr = Array.set arr i v; arr

module List =
    /// Curried cons
    let inline cons hd tl = hd::tl
  
    let inline singleton x = [x]

    let inline lift2 f (l1: _ list) (l2: _ list) = 
        [ for i in l1 do
            for j in l2 do
                yield f i j ]

    let span pred l =
        let rec loop l cont =
            match l with
            | [] -> ([],[])
            | x::[] when pred x -> (cont l, [])
            | x::xs when not (pred x) -> (cont [], l)
            | x::xs when pred x -> loop xs (fun rest -> cont (x::rest))
            | _ -> failwith "Unrecognized pattern"
        loop l id
  
    let split pred l = span (not << pred) l
  
    let skipWhile pred l = span pred l |> snd
    let skipUntil pred l = split pred l |> snd
    let takeWhile pred l = span pred l |> fst
    let takeUntil pred l = split pred l |> fst
    
    let splitAt n l =
        let pred i = i >= n
        let rec loop i l cont =
            match l with
            | [] -> ([],[])
            | x::[] when not (pred i) -> (cont l, [])
            | x::xs when pred i -> (cont [], l)
            | x::xs when not (pred i) -> loop (i+1) xs (fun rest -> cont (x::rest))
            | _ -> failwith "Unrecognized pattern"
        loop 0 l id
  
    let skip n l = splitAt n l |> snd
    let take n l = splitAt n l |> fst

    let inline mapIf pred f =
        List.map (fun x -> if pred x then f x else x)


/// The DList is an implementation of John Hughes' append list.
/// See http://dl.acm.org/citation.cfm?id=8475 for more information.
/// This implementation adds an additional parameter to allow a more
/// efficient calculation of the list length.
/// Note that an alternate form would represent the DList as:
/// type DList<'a> = DList of ('a list -> 'a list)
/// An example can be found at http://stackoverflow.com/questions/5324623/functional-o1-append-and-on-iteration-from-first-element-list-data-structure/5327209#5327209
type DList<'a> =
    | Nil
    | Unit of 'a
    | Join of DList<'a> * DList<'a> * int (* the total length of the DList *)
    with

    static member op_Equality(left, right) =
        match left with
        | Nil -> match right with Nil -> true | _ -> false
        | Unit x -> match right with Unit y -> x = y | _ -> false
        | Join(x,y,l) ->
            match right with
            | Join(x',y',l') -> l = l' && x = x' && y = y' // TODO: || iterate each and compare the values.
            | _ -> false 

    static member op_Nil() = Nil

    static member op_Cons(hd, tl) =
        match tl with
        | Nil -> Unit hd
        | _ -> Join(Unit hd, tl, tl.Length + 1)

    static member op_Append(left, right) =
        match left with
        | Nil -> right
        | _ -> match right with
               | Nil -> left
               | _ -> Join(left, right, left.Length + right.Length)

    member x.IsEmpty = match x with Nil -> true | _ -> false

    member x.Length =
        match x with
        | Nil -> 0 
        | Unit _ -> 1
        | Join(_,_,l) -> l

    member x.Head =
        match x with
        | Unit x' -> x'
        | Join(x',y,_) -> x'.Head
        | _ -> failwith "DList.head: empty list"

    member x.Tail =
        let rec step (xs:DList<'a>) (acc:DList<'a>) : DList<'a> =
            match xs with
            | Nil -> acc
            | Unit _ -> acc
            | Join(x,y,_) -> step x (DList<_>.op_Append(y, acc))
        if x.IsEmpty then Nil else step x Nil

    interface IEnumerable<'a> with
        member x.GetEnumerator() =
            let enumerable = seq {
                match x with
                | Nil -> () 
                | Unit x -> yield x
                | Join(x,y,_) ->
                    yield! x :> seq<'a>
                    yield! y :> seq<'a> }
            enumerable.GetEnumerator()

        member x.GetEnumerator() =
            let enumerable = seq {
                match x with
                | Nil -> () 
                | Unit x -> yield x
                | Join(x,y,_) ->
                    yield! x :> seq<'a>
                    yield! y :> seq<'a> }
            enumerable.GetEnumerator() :> IEnumerator

module DList =
    let empty<'a> : DList<'a> = Nil

    let isEmpty (l:DList<_>) = l.IsEmpty

    let length (l:DList<_>) = l.Length

    let singleton x = Unit x

    let ofSeq s = Seq.fold (fun xs x ->
        match xs with 
        | Nil -> Unit x
        | Unit _ -> Join(xs, Unit x, 2)
        | Join(_,_,l) -> Join(xs, Unit x, l+1)) Nil s

    let toSeq (l:DList<_>) = l :> seq<_>

    let cons hd tl = DList<_>.op_Cons(hd, tl)

    let append right left = DList<_>.op_Append(left, right)

    let head (l:DList<_>) = l.Head

    let tail (l:DList<_>) = l.Tail

    /// Fold walks the DList using constant stack space. Implementation is from Norman Ramsey.
    /// See http://stackoverflow.com/questions/5324623/functional-o1-append-and-on-iteration-from-first-element-list-data-structure/5334068#5334068
    let fold f seed l =
        let rec walk lefts l xs =
            match l with
            | Nil         -> finish lefts xs
            | Unit x      -> finish lefts <| f xs x
            | Join(x,y,_) -> walk (x::lefts) y xs
        and finish lefts xs =
            match lefts with
            | []    -> xs
            | t::ts -> walk ts t xs
        in walk [] l seed

    let toList l = fold (flip List.cons) [] l

    let toArray l = l |> toList |> Array.ofList


/// An ArraySegment with structural comparison and equality.
[<CustomEquality; CustomComparison; SerializableAttribute; StructAttribute>]
type ArraySegment<'a when 'a : comparison> =
    val Array: 'a[]
    val Offset: int
    val Count: int
    new (array: 'a[]) = { Array = array; Offset = 0; Count = array.Length }
    new (array: 'a[], offset: int, count: int) = { Array = array; Offset = offset; Count = count }

    /// Compares two array segments based on their structure.
    static member Compare (a:ArraySegment<_>, b:ArraySegment<_>) =
        let x,o,l = a.Array, a.Offset, a.Count
        let x',o',l' = b.Array, b.Offset, b.Count
        if x = x' && o = o' && l = l' then 0
        elif x = x' then
            if o = o' then if l < l' then -1 else 1
            else if o < o' then -1 else 1 
        else let left, right = x.[o..(o+l-1)], x'.[o'..(o'+l'-1)] in
             if left = right then 0 elif left < right then -1 else 1

    /// Compares two objects for equality. When both are array segments, structural equality is used.
    override x.Equals(other) = 
        match other with
        | :? ArraySegment<'a> as other' -> ArraySegment.Compare(x, other') = 0
        | _ -> false

    /// Gets the hash code for the array segment.
    override x.GetHashCode() = hash x

    /// Gets an enumerator for the contents stored in the array segment.
    member x.GetEnumerator() =
        if x.Count = 0 then Enumerator.empty<_>
        else
            let segment = x.Array
            let minIndex = x.Offset
            let maxIndex = x.Offset + x.Count - 1
            let currentIndex = ref <| minIndex - 1
            { new IEnumerator<_> with 
                member self.Current =
                    if !currentIndex < minIndex then
                        invalidOp "Enumeration has not started. Call MoveNext."
                    elif !currentIndex > maxIndex then
                        invalidOp "Enumeration already finished."
                    else segment.[!currentIndex]
              interface System.Collections.IEnumerator with
                member self.Current =
                    if !currentIndex < minIndex then
                        invalidOp "Enumeration has not started. Call MoveNext."
                    elif !currentIndex > maxIndex then
                        invalidOp "Enumeration already finished."
                    else box segment.[!currentIndex]
                member self.MoveNext() = 
                    if !currentIndex < maxIndex then
                        incr currentIndex
                        true
                    else false
                member self.Reset() = currentIndex := minIndex - 1
              interface System.IDisposable with 
                member self.Dispose() = () }

    interface System.IComparable with
        member x.CompareTo(other) =
            if other = null then 1
            else
                match other with
                | :? ArraySegment<'a> as other' -> ArraySegment.Compare(x, other')
                | _ -> -1

    interface System.Collections.Generic.IEnumerable<'a> with
        /// Gets an enumerator for the contents stored in the array segment.
        member x.GetEnumerator() = x.GetEnumerator()
        /// Gets an enumerator for the contents stored in the array segment.
        member x.GetEnumerator() = x.GetEnumerator() :> IEnumerator

    member x.IsEmpty = 
        #if NET40
        Contract.Requires(x.Count >= 0)
        #else
        Debug.Assert(x.Count >= 0)
        #endif
        x.Count <= 0

    member x.Item(pos) =
        #if NET40
        Contract.Requires(x.Offset + pos <= x.Count)
        #else
        Debug.Assert(x.Offset + pos <= x.Count)
        #endif
        x.Array.[x.Offset + pos]

    member x.Head =
        if x.Count <= 0 then
          failwith "Cannot take the head of an empty array segment."
        else x.Array.[x.Offset]

    member x.Tail =
        #if NET40
        Contract.Requires(x.Count >= 1)
        #else
        Debug.Assert(x.Count >= 1)
        #endif
        if x.Count = 1 then ArraySegment.op_Nil()
        else ArraySegment<_>(x.Array, x.Offset+1, x.Count-1)
        
    static member Empty = ArraySegment<'a>()

    static member op_Nil() = ArraySegment<'a>.Empty
    
    /// Please note that a new array is created and both the head and tail are copied in,
    /// disregarding any additional contents in the original tail array.
    static member op_Cons(hd, segment:ArraySegment<'a>) =
        let x,o,l = segment.Array, segment.Offset, segment.Count in
        if l = 0 then ArraySegment<'a>([|hd|])
        else let buffer = Array.zeroCreate (l + 1)
             Array.blit [|hd|] 0 buffer 0 1
             Array.blit x o buffer 1 l
             ArraySegment<'a>(buffer,0,l+1)
    
    /// Please note that a new array is created and both arrays are copied in,
    /// disregarding any additional contents in the original, underlying arrays.
    static member op_Append(x:ArraySegment<'a>, y:ArraySegment<'a>) = 
        if x.IsEmpty then y
        elif y.IsEmpty then x
        else let x,o,l = x.Array, x.Offset, x.Count
             let x',o',l' = y.Array, y.Offset, y.Count
             let buffer = Array.zeroCreate<_> (l + l')
             Array.blit x o buffer 1 l
             Array.blit x' o' buffer l l'
             ArraySegment<'a>(buffer,0,l+l')
  
module ArraySegment =
    /// An active pattern for conveniently retrieving the properties of a ArraySegment<_>.
    let (|Segment|) (x:ArraySegment<_>) = x.Array, x.Offset, x.Count
    
    let empty<'a> = ArraySegment.Empty
    let singleton c = ArraySegment<_>(Array.create 1 c, 0, 1)
    let create arr = ArraySegment<_>(arr, 0, arr.Length)
    let findIndex pred (segment:ArraySegment<_>) =
        Array.FindIndex(segment.Array, segment.Offset, segment.Count, Predicate<_>(pred))
    let ofArraySegment (segment:ArraySegment<_>) = ArraySegment<_>(segment.Array, segment.Offset, segment.Count)
    let ofSeq s = let arr = Array.ofSeq s in ArraySegment<_>(arr, 0, arr.Length)
    let ofList l = ArraySegment<_>(Array.ofList l, 0, l.Length)
    let ofString (s:string) = s.ToCharArray() |> Array.map byte |> create
    let toArray (segment:ArraySegment<_>) =
        if segment.Count = 0 then Array.empty<_>
        else segment.Array.[segment.Offset..(segment.Offset + segment.Count - 1)]
    let toSeq (segment:ArraySegment<_>) = segment :> seq<byte>
    let toList (segment:ArraySegment<_>) = List.ofSeq segment
    let toString (segment:ArraySegment<_>) = System.Text.Encoding.ASCII.GetString(segment.Array, segment.Offset, segment.Count)
    let isEmpty (segment:ArraySegment<_>) = segment.IsEmpty
    let length (segment:ArraySegment<_>) = segment.Count
    let index (segment:ArraySegment<_>) pos = segment.[pos]
    let head (segment:ArraySegment<_>) = segment.Head
    let tail (segment:ArraySegment<_>) = segment.Tail
    
    /// cons uses Buffer.SetByte and Buffer.BlockCopy for efficient array operations.
    /// Please note that a new array is created and both the head and tail are copied in,
    /// disregarding any additional bytes in the original tail array.
    let cons hd (segment:ArraySegment<_>) = ArraySegment.op_Cons(hd, segment)
    
    /// append uses Buffer.BlockCopy for efficient array operations.
    /// Please note that a new array is created and both arrays are copied in,
    /// disregarding any additional bytes in the original, underlying arrays.
    let append b a = ArraySegment.op_Append(a, b)
    
    let fold f seed segment =
        let rec loop segment acc =
            if isEmpty segment then acc 
            else let hd, tl = head segment, tail segment
                 loop tl (f acc hd)
        loop segment seed
  
    let split pred (segment: ArraySegment<_>) =
        if isEmpty segment then empty, empty
        else let index = findIndex pred segment
             if index = -1 then segment, empty
             else let count = index - segment.Offset
                  ArraySegment<_>(segment.Array, segment.Offset, count),
                  ArraySegment<_>(segment.Array, index, segment.Count - count)
    
    let span pred segment = split (not << pred) segment
    
    let splitAt n (segment:ArraySegment<_>) =
        #if NET40
        Contract.Requires(n >= 0)
        #else
        Debug.Assert(n >= 0)
        #endif
        if isEmpty segment then empty, empty
        elif n = 0 then empty, segment
        elif n >= segment.Count then segment, empty
        else let x,o,l = segment.Array, segment.Offset, segment.Count in ArraySegment<_>(x,o,n), ArraySegment<_>(x,o+n,l-n)
    
    let skip n segment = splitAt n segment |> snd
    let skipWhile pred segment = span pred segment |> snd
    let skipUntil pred segment = split pred segment |> snd
    let take n segment = splitAt n segment |> fst 
    let takeWhile pred segment = span pred segment |> fst
    let takeUntil pred segment = split pred segment |> fst 


/// An ArraySegment of bytes with structural comparison and equality.
[<CustomEquality; CustomComparison; SerializableAttribute; StructAttribute>]
type BS =
    val Array: byte[]
    val Offset: int
    val Count: int
    new (array: byte[]) = { Array = array; Offset = 0; Count = array.Length }
    new (array: byte[], offset: int, count: int) = { Array = array; Offset = offset; Count = count }

    /// Compares two byte strings based on their structure.
    static member Compare (a:BS, b:BS) =
        let x,o,l = a.Array, a.Offset, a.Count
        let x',o',l' = b.Array, b.Offset, b.Count
        if x = x' && o = o' && l = l' then 0
        elif x = x' then
            if o = o' then if l < l' then -1 else 1
            else if o < o' then -1 else 1 
        else let left, right = x.[o..(o+l-1)], x'.[o'..(o'+l'-1)] in
             if left = right then 0 elif left < right then -1 else 1

    /// Compares two objects for equality. When both are byte strings, structural equality is used.
    override x.Equals(other) = 
        match other with
        | :? BS as other' -> BS.Compare(x, other') = 0
        | _ -> false

    /// Gets the hash code for the byte string.
    override x.GetHashCode() = hash x

    /// Gets an enumerator for the bytes stored in the byte string.
    member x.GetEnumerator() =
        if x.Count = 0 then Enumerator.empty<_>
        else
            let segment = x.Array
            let minIndex = x.Offset
            let maxIndex = x.Offset + x.Count - 1
            let currentIndex = ref <| minIndex - 1
            { new IEnumerator<_> with 
                member self.Current =
                    if !currentIndex < minIndex then
                        invalidOp "Enumeration has not started. Call MoveNext."
                    elif !currentIndex > maxIndex then
                        invalidOp "Enumeration already finished."
                    else segment.[!currentIndex]
              interface System.Collections.IEnumerator with
                member self.Current =
                    if !currentIndex < minIndex then
                        invalidOp "Enumeration has not started. Call MoveNext."
                    elif !currentIndex > maxIndex then
                        invalidOp "Enumeration already finished."
                    else box segment.[!currentIndex]
                member self.MoveNext() = 
                    if !currentIndex < maxIndex then
                        incr currentIndex
                        true
                    else false
                member self.Reset() = currentIndex := minIndex - 1
              interface System.IDisposable with 
                member self.Dispose() = () }

    interface System.IComparable with
        member x.CompareTo(other) =
            if other = null then 1
            else
                match other with
                | :? BS as other' -> BS.Compare(x, other')
                | _ -> -1

    interface System.Collections.Generic.IEnumerable<byte> with
        /// Gets an enumerator for the bytes stored in the byte string.
        member x.GetEnumerator() = x.GetEnumerator()
        /// Gets an enumerator for the bytes stored in the byte string.
        member x.GetEnumerator() = x.GetEnumerator() :> IEnumerator

    member x.IsEmpty = 
        #if NET40
        Contract.Requires(x.Count >= 0)
        #else
        Debug.Assert(x.Count >= 0)
        #endif
        x.Count <= 0

    member x.Item(pos) =
        #if NET40
        Contract.Requires(x.Offset + pos <= x.Count)
        #else
        Debug.Assert(x.Offset + pos <= x.Count)
        #endif
        x.Array.[x.Offset + pos]

    member x.Head =
        if x.Count <= 0 then
          failwith "Cannot take the head of an empty byte string."
        else x.Array.[x.Offset]

    member x.Tail =
        #if NET40
        Contract.Requires(x.Count >= 1)
        #else
        Debug.Assert(x.Count >= 1)
        #endif
        if x.Count = 1 then BS.op_Nil()
        else BS(x.Array, x.Offset+1, x.Count-1)
        
    static member Empty = BS()

    static member op_Nil() = BS.Empty
    
    /// cons uses Buffer.SetByte and Buffer.BlockCopy for efficient array operations.
    /// Please note that a new array is created and both the head and tail are copied in,
    /// disregarding any additional bytes in the original tail array.
    static member op_Cons(hd, bs:BS) =
        let x,o,l = bs.Array, bs.Offset, bs.Count in
        if l = 0 then BS [|hd|]
        else let buffer = Array.zeroCreate<byte> (l + 1)
             Buffer.SetByte(buffer,0,hd)
             Buffer.BlockCopy(x,o,buffer,1,l)
             BS(buffer,0,l+1)
    
    /// append uses Buffer.BlockCopy for efficient array operations.
    /// Please note that a new array is created and both arrays are copied in,
    /// disregarding any additional bytes in the original, underlying arrays.
    static member op_Append(x:BS, y:BS) = 
        if x.IsEmpty then y
        elif y.IsEmpty then x
        else let x,o,l = x.Array, x.Offset, x.Count
             let x',o',l' = y.Array, y.Offset, y.Count
             let buffer = Array.zeroCreate<byte> (l + l')
             Buffer.BlockCopy(x,o,buffer,0,l)
             Buffer.BlockCopy(x',o',buffer,l,l')
             BS(buffer,0,l+l')
  
module ByteString =
    /// An active pattern for conveniently retrieving the properties of a BS.
    let (|BS|) (x:BS) = x.Array, x.Offset, x.Count
    
    let empty = BS.Empty
    let singleton c = BS(Array.create 1 c, 0, 1)
    let create arr = BS(arr, 0, arr.Length)
    let findIndex pred (bs:BS) =
        Array.FindIndex(bs.Array, bs.Offset, bs.Count, Predicate<_>(pred))
    let ofArraySegment (segment:ArraySegment<byte>) = BS(segment.Array, segment.Offset, segment.Count)
    let ofSeq s = let arr = Array.ofSeq s in BS(arr, 0, arr.Length)
    let ofList l = BS(Array.ofList l, 0, l.Length)
    let ofString (s:string) = s.ToCharArray() |> Array.map byte |> create
    let toArray (bs:BS) =
        if bs.Count = 0 then Array.empty<_>
        else bs.Array.[bs.Offset..(bs.Offset + bs.Count - 1)]
    let toSeq (bs:BS) = bs :> seq<byte>
    let toList (bs:BS) = List.ofSeq bs
    let toString (bs:BS) = System.Text.Encoding.ASCII.GetString(bs.Array, bs.Offset, bs.Count)
    let isEmpty (bs:BS) = bs.IsEmpty
    let length (bs:BS) = bs.Count
    let index (bs:BS) pos = bs.[pos]
    let head (bs:BS) = bs.Head
    let tail (bs:BS) = bs.Tail
    
    /// cons uses Buffer.SetByte and Buffer.BlockCopy for efficient array operations.
    /// Please note that a new array is created and both the head and tail are copied in,
    /// disregarding any additional bytes in the original tail array.
    let cons hd (bs:BS) = BS.op_Cons(hd, bs)
    
    /// append uses Buffer.BlockCopy for efficient array operations.
    /// Please note that a new array is created and both arrays are copied in,
    /// disregarding any additional bytes in the original, underlying arrays.
    let append b a = BS.op_Append(a, b)
    
    let fold f seed bs =
        let rec loop bs acc =
            if isEmpty bs then acc 
            else let hd, tl = head bs, tail bs
                 loop tl (f acc hd)
        loop bs seed
  
    let split pred (bs:BS) =
        if isEmpty bs then empty, empty
        else let index = findIndex pred bs
             if index = -1 then bs, empty
             else let count = index - bs.Offset
                  BS(bs.Array, bs.Offset, count),
                  BS(bs.Array, index, bs.Count - count)
    
    let span pred bs = split (not << pred) bs
    
    let splitAt n (bs:BS) =
        #if NET40
        Contract.Requires(n >= 0)
        #else
        Debug.Assert(n >= 0)
        #endif
        if isEmpty bs then empty, empty
        elif n = 0 then empty, bs
        elif n >= bs.Count then bs, empty
        else let x,o,l = bs.Array, bs.Offset, bs.Count in BS(x,o,n), BS(x,o+n,l-n)
    
    let skip n bs = splitAt n bs |> snd
    let skipWhile pred bs = span pred bs |> snd
    let skipUntil pred bs = split pred bs |> snd
    let take n bs = splitAt n bs |> fst 
    let takeWhile pred bs = span pred bs |> fst
    let takeUntil pred bs = split pred bs |> fst 

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Dictionary =
    let tryFind key (d: IDictionary<_,_>) =
        match d.TryGetValue key with
        | true,v -> Some v
        | _ -> None

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
[<Extension>]
module NameValueCollection =
    open System.Collections.Specialized
    open System.Linq

    /// <summary>
    /// Returns a new <see cref="NameValueCollection"/> with the concatenation of two <see cref="NameValueCollection"/>s
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    [<Extension>]
    [<CompiledName("Concat")>]
    let concat a b = 
        let x = NameValueCollection()
        x.Add a
        x.Add b
        x

    /// <summary>
    /// In-place add of a key-value pair to a <see cref="NameValueCollection"/>
    /// </summary>
    /// <param name="x"></param>
    /// <param name="a"></param>
    /// <param name="b"></param>
    let inline addInPlace (x: NameValueCollection) (a,b) = x.Add(a,b)

    /// Adds an element to a copy of an existing NameValueCollection
    let add name value (x: NameValueCollection) =
        let r = NameValueCollection x
        r.Add(name,value)
        r

    /// <summary>
    /// Returns a <see cref="NameValueCollection"/> as a sequence of key-value pairs.
    /// Note that keys may be duplicated.
    /// </summary>
    /// <param name="a"></param>
    [<Extension>]
    [<CompiledName("ToEnumerable")>]
    let toSeq (a: NameValueCollection) =
        a.AllKeys
        |> Seq.collect (fun k -> a.GetValues k |> Seq.map (fun v -> k,v))

    /// <summary>
    /// Returns a <see cref="NameValueCollection"/> as a list of key-value pairs.
    /// Note that keys may be duplicated.
    /// </summary>
    /// <param name="a"></param>
    let inline toList a = toSeq a |> Seq.toList

    /// <summary>
    /// Creates a <see cref="NameValueCollection"/> from a list of key-value pairs
    /// </summary>
    /// <param name="l"></param>
    let fromSeq l =
        let x = NameValueCollection()
        Seq.iter (addInPlace x) l
        x

    [<Extension>]
    [<CompiledName("ToLookup")>]
    let toLookup a =
        let s = toSeq a
        s.ToLookup(fst, snd)

    [<Extension>]
    [<CompiledName("AsDictionary")>]
    let asDictionary (x: NameValueCollection) =
        let notimpl() = raise <| NotImplementedException()
        let getEnumerator() =
            let enum = x.GetEnumerator()
            let wrapElem (o: obj) = 
                let key = o :?> string
                let values = x.GetValues key
                KeyValuePair(key,values)
            { new IEnumerator<KeyValuePair<string,string[]>> with
                member e.Current = wrapElem enum.Current
                member e.MoveNext() = enum.MoveNext()
                member e.Reset() = enum.Reset()
                member e.Dispose() = ()
                member e.Current = box (wrapElem enum.Current) }
        { new IDictionary<string,string[]> with
            member d.Count = x.Count
            member d.IsReadOnly = false 
            member d.Item 
                with get k = 
                    let v = x.GetValues k
                    if v = null
                        then raise <| KeyNotFoundException(sprintf "Key '%s' not found" k)
                        else v
                and set k v =
                    x.Remove k
                    for i in v do
                        x.Add(k,i)
            member d.Keys = upcast ResizeArray<string>(x.Keys |> Seq.cast)
            member d.Values = 
                let values = ResizeArray<string[]>()
                for i in 0..x.Count-1 do
                    values.Add(x.GetValues i)
                upcast values
            member d.Add v = d.Add(v.Key, v.Value)
            member d.Add(key,value) = 
                if key = null
                    then raise <| ArgumentNullException("key")
                if d.ContainsKey key
                    then raise <| ArgumentException(sprintf "Duplicate key '%s'" key, "key")
                d.[key] <- value
            member d.Clear() = x.Clear()
            member d.Contains item = x.GetValues(item.Key) = item.Value
            member d.ContainsKey key = x.[key] <> null
            member d.CopyTo(array,arrayIndex) = notimpl()
            member d.GetEnumerator() = getEnumerator()
            member d.GetEnumerator() = getEnumerator() :> IEnumerator
            member d.Remove (item: KeyValuePair<string,string[]>) = 
                if d.Contains item then
                    x.Remove item.Key
                    true
                else
                    false
            member d.Remove (key: string) = 
                let exists = d.ContainsKey key
                x.Remove key
                exists
            member d.TryGetValue(key: string, value: byref<string[]>) = 
                if d.ContainsKey key then
                    value <- d.[key]
                    true
                else false
            }

    [<Extension>]
    [<CompiledName("AsReadonlyDictionary")>]
    let asReadonlyDictionary x =
        let a = asDictionary x
        let notSupported() = raise <| NotSupportedException("Readonly dictionary")
        { new IDictionary<string,string[]> with
            member d.Count = a.Count
            member d.IsReadOnly = true
            member d.Item 
                with get k = a.[k]
                and set k v = notSupported()
            member d.Keys = a.Keys
            member d.Values = a.Values
            member d.Add v = notSupported()
            member d.Add(key,value) = notSupported()
            member d.Clear() = notSupported()
            member d.Contains item = a.Contains item
            member d.ContainsKey key = a.ContainsKey key
            member d.CopyTo(array,arrayIndex) = a.CopyTo(array,arrayIndex)
            member d.GetEnumerator() = a.GetEnumerator()
            member d.GetEnumerator() = a.GetEnumerator() :> IEnumerator
            member d.Remove (item: KeyValuePair<string,string[]>) = notSupported(); false
            member d.Remove (key: string) = notSupported(); false
            member d.TryGetValue(key: string, value: byref<string[]>) = a.TryGetValue(key, ref value)
        }                

    [<Extension>]
    [<CompiledName("AsLookup")>]
    let asLookup (this: NameValueCollection) =
        let getEnumerator() = 
            let e = this.GetEnumerator()
            let wrapElem (o: obj) = 
                let key = o :?> string
                let values = this.GetValues key :> seq<string>
                { new IGrouping<string,string> with
                    member x.Key = key
                    member x.GetEnumerator() = values.GetEnumerator()
                    member x.GetEnumerator() = values.GetEnumerator() :> IEnumerator }
  
            { new IEnumerator<IGrouping<string,string>> with
                member x.Current = wrapElem e.Current
                member x.MoveNext() = e.MoveNext()
                member x.Reset() = e.Reset()
                member x.Dispose() = ()
                member x.Current = box (wrapElem e.Current) }
                      
        { new ILookup<string,string> with
            member x.Count = this.Count
            member x.Item 
                with get key = 
                    match this.GetValues key with
                    | null -> Seq.empty
                    | a -> upcast a
            member x.Contains key = this.Get key <> null
            member x.GetEnumerator() = getEnumerator()
            member x.GetEnumerator() = getEnumerator() :> IEnumerator }
