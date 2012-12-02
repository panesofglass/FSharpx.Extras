// First version copied from the F# Power Pack 
// https://raw.github.com/fsharp/powerpack/master/src/FSharp.PowerPack/LazyList.fsi

// (c) Microsoft Corporation 2005-2009. 

namespace FSharpx

open System.Collections.Generic

/// LazyLists are possibly-infinite, cached sequences.  See also IEnumerable/Seq for
/// uncached sequences. LazyLists normally involve delayed computations without 
/// side-effects.  The results of these computations are cached and evaluations will be 
/// performed only once for each element of the lazy list.  In contrast, for sequences 
/// (IEnumerable) recomputation happens each time an enumerator is created and the sequence 
/// traversed.
///
/// LazyLists can represent cached, potentially-infinite computations.  Because they are 
/// cached they may cause memory leaks if some active code or data structure maintains a 
/// live reference to the head of an infinite or very large lazy list while iterating it, 
/// or if a reference is maintained after the list is no longer required.
///
/// Lazy lists may be matched using the LazyList.Cons and LazyList.Nil active patterns. 
/// These may force the computation of elements of the list.

[<Sealed>]
type LazyList<'T> =
    interface IEnumerable<'T>
    interface System.Collections.IEnumerable
    

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module LazyList =

    /// Test if a list is empty.  Forces the evaluation of
    /// the first element of the stream if it is not already evaluated.
    val isEmpty: LazyList<'T> -> bool

    /// Return the first element of the list.  Forces the evaluation of
    /// the first cell of the list if it is not already evaluated.
    val head       : LazyList<'T> -> 'T

    /// Return the list corresponding to the remaining items in the sequence.  
    /// Forces the evaluation of the first cell of the list if it is not already evaluated.
    val tail       : LazyList<'T> -> LazyList<'T>

    /// Get the first cell of the list.
    val get      : LazyList<'T> -> ('T * LazyList<'T>) option  

    /// Return the list which on consumption will consist of at most 'n' elements of 
    /// the input list.  
    val take     : count:int -> source:LazyList<'T> -> LazyList<'T>

    /// Return the list which on consumption will skip the first 'n' elements of 
    /// the input list.  
    val skip     : count:int -> source:LazyList<'T> -> LazyList<'T>

    /// Apply the given function to successive elements of the list, returning the first
    /// result where function returns <c>Some(x)</c> for some x. If the function never returns
    /// true, 'None' is returned.
    val tryFind    : predicate:('T -> bool) -> source:LazyList<'T> -> 'T option

    /// Return the first element for which the given function returns <c>true</c>.
    /// Raise <c>KeyNotFoundException</c> if no such element exists.
    val find     : predicate:('T -> bool) -> source:LazyList<'T> -> 'T 

    /// Evaluates to the list that contains no items
    [<GeneralizableValue>]
    val empty<'T>    : LazyList<'T>

    /// Return the length of the list
    val length: list:LazyList<'T> -> int

    /// Return a new list which contains the given item followed by the
    /// given list.
    val cons     : 'T -> LazyList<'T>               -> LazyList<'T>

    /// Return a new list which on consumption contains the given item 
    /// followed by the list returned by the given computation.  The 
    val consDelayed    : 'T -> (unit -> LazyList<'T>)     -> LazyList<'T>

    /// Return the list which on consumption will consist of an infinite sequence of 
    /// the given item
    val repeat   : 'T -> LazyList<'T>

    /// Return a list that is in effect the list returned by the given computation.
    /// The given computation is not executed until the first element on the list is
    /// consumed.
    val delayed  : (unit -> LazyList<'T>)           -> LazyList<'T>

    /// Return a list that contains the elements returned by the given computation.
    /// The given computation is not executed until the first element on the list is
    /// consumed.  The given argument is passed to the computation.  Subsequent elements
    /// in the list are generated by again applying the residual 'b to the computation.
    val unfold   : ('State -> ('T * 'State) option) -> 'State -> LazyList<'T>

    /// Return the list which contains on demand the elements of the first list followed
    /// by the elements of the second list
    val append   : LazyList<'T> -> source:LazyList<'T> -> LazyList<'T>

    /// Return the list which contains on demand the pair of elements of the first and second list
    val zip  : LazyList<'T1> -> LazyList<'T2> -> LazyList<'T1 * 'T2>

    /// Return the list which contains on demand the list of elements of the list of lazy lists.
    val concat   : LazyList< LazyList<'T>> -> LazyList<'T>

    /// Return a new collection which on consumption will consist of only the elements of the collection
    /// for which the given predicate returns "true"
    val filter   : predicate:('T -> bool) -> source:LazyList<'T> -> LazyList<'T>

    /// Apply the given function to each element of the collection. 
    val iter: action:('T -> unit) -> list:LazyList<'T>-> unit

    /// Return a new list consisting of the results of applying the given accumulating function
    /// to successive elements of the list
    val scan    : folder:('State -> 'T -> 'State) -> 'State -> source:LazyList<'T> -> LazyList<'State>  

    /// Build a new collection whose elements are the results of applying the given function
    /// to each of the elements of the collection.
    val map      : mapping:('T -> 'U) -> source:LazyList<'T> -> LazyList<'U>

    /// Build a new collection whose elements are the results of applying the given function
    /// to the corresponding elements of the two collections pairwise.
    val map2     : mapping:('T1 -> 'T2 -> 'U) -> LazyList<'T1> -> LazyList<'T2> -> LazyList<'U>

    /// Build a collection from the given array. This function will eagerly evaluate all of the 
    /// list (and thus may not terminate). 
    val ofArray : 'T array -> LazyList<'T>

    /// Build an array from the given collection
    val toArray : LazyList<'T> -> 'T array

    /// Build a collection from the given list. This function will eagerly evaluate all of the 
    /// list (and thus may not terminate). 
    val ofList  : list<'T> -> LazyList<'T>

    /// Build a non-lazy list from the given collection. This function will eagerly evaluate all of the 
    /// list (and thus may not terminate). 
    val toList  : LazyList<'T> -> list<'T>

    /// Return a view of the collection as an enumerable object
    val toSeq: LazyList<'T> -> seq<'T>

    /// Build a new collection from the given enumerable object
    val ofSeq: seq<'T> -> LazyList<'T>

    [<System.Obsolete("This function has been renamed. Use 'LazyList.consDelayed' instead")>]
    val consf    : 'T -> (unit -> LazyList<'T>)     -> LazyList<'T>

    [<System.Obsolete("This function has been renamed. Use 'LazyList.head' instead")>]
    val hd: LazyList<'T> -> 'T

    [<System.Obsolete("This function has been renamed. Use 'LazyList.tail' instead")>]
    val tl     : LazyList<'T> -> LazyList<'T>

    [<System.Obsolete("This function will be removed. Use 'not (LazyList.isEmpty list)' instead")>]
    val nonempty : LazyList<'T> -> bool

    [<System.Obsolete("This function has been renamed. Use 'LazyList.skip' instead")>]
    val drop     : count:int -> source:LazyList<'T> -> LazyList<'T>

    [<System.Obsolete("This function has been renamed to 'tryFind'")>]
    val first    : predicate:('T -> bool) -> source:LazyList<'T> -> 'T option

    [<System.Obsolete("This function has been renamed. Use 'LazyList.ofArray' instead")>]
    val of_array : 'T array -> LazyList<'T>

    [<System.Obsolete("This function has been renamed. Use 'LazyList.toArray' instead")>]
    val to_array : LazyList<'T> -> 'T array

    [<System.Obsolete("This function has been renamed. Use 'LazyList.ofList' instead")>]
    val of_list  : list<'T> -> LazyList<'T>

    [<System.Obsolete("This function has been renamed. Use 'LazyList.toList' instead")>]
    val to_list  : LazyList<'T> -> list<'T>

    [<System.Obsolete("This function has been renamed. Use 'LazyList.toSeq' instead")>]
    val to_seq: LazyList<'T> -> seq<'T>

    [<System.Obsolete("This function has been renamed. Use 'LazyList.ofSeq' instead")>]
    val of_seq: seq<'T> -> LazyList<'T>

    [<System.Obsolete("This function has been renamed to 'scan'")>]
    val folds    : folder:('State -> 'T -> 'State) -> 'State -> source:LazyList<'T> -> LazyList<'State>  

    [<System.Obsolete("This function has been renamed to 'zip'")>]
    val combine  : LazyList<'T1> -> LazyList<'T2> -> LazyList<'T1 * 'T2>

    //--------------------------------------------------------------------------
    // Lazy list active patterns

    val (|Cons|Nil|) : LazyList<'T> -> Choice<('T * LazyList<'T>),unit>

