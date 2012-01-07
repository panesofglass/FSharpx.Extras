namespace FSharpx
#nowarn "40"

open System.Collections
open System.Collections.Generic

(*
# Iteratee

The *Iteratee* module, part of the [FSharpx](http://github.com/fsharp/fsharpx) library, provides a set of types and functions for building compositional, input processing components.

## System.IO.Stream-based processing

The System.IO.Stream type should be familiar to anyone who has ever worked with I/O in .NET. Streams are the primary abstraction available for working with streams of data, whether over the file system (FileStream) or network protocols (NetworkStream). Streams also have a nice support structure in the form of TextReaders and TextWriters, among other, similar types.

A common scenario for I/O processing is parsing an HTTP request message. Granted, most will rely on ASP.NET, HttpListener, or WCF to do this for them. However, HTTP request parsing has a lot of interesting elements that are useful for demonstrating problem areas in inefficient resource usage using other techniques. For our running sample, we'll focus on parsing the headers of the following HTTP request message:

    let httpRequest : byte [] = @"GET /some/uri HTTP/1.1
    Accept:text/html,application/xhtml+xml,application/xml
    Accept-Charset:ISO-8859-1,utf-8;q=0.7,*;q=0.3
    Accept-Encoding:gzip,deflate,sdch
    Accept-Language:en-US,en;q=0.8
    Cache-Control:max-age=0
    Connection:keep-alive
    Host:stackoverflow.com
    If-Modified-Since:Sun, 25 Sep 2011 20:55:29 GMT
    Referer:http://www.bing.com/search?setmkt=en-US&q=don't+use+IEnumerable%3Cbyte%3E
    User-Agent:Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/535.4 (KHTML, like Gecko) Chrome/16.0.889.0 Safari/535.4

    <!DOCTYPE HTML PUBLIC ""-//W3C//DTD HTML 4.01//EN"" ""http://www.w3.org/TR/html4/strict.dtd"">
    <html>
    <head>
    ...
    </head>
    <body>
    ...
    </body>
    </html>"B

Using the standard Stream processing apis, you might write something like the following:

    let rec readConsecutiveLines (reader:System.IO.StreamReader) cont =
        if reader.EndOfStream then cont []
        else let line = reader.ReadLine()
             if System.String.IsNullOrEmpty(line) then cont []
             else readConsecutiveLines reader (fun tail -> cont (line::tail))

    let readFromStream() =
        let sw = System.Diagnostics.Stopwatch.StartNew()
        let result =
            [ for _ in 1..10000 do 
                use stream = new System.IO.MemoryStream(httpRequest)
                use reader = new System.IO.StreamReader(stream)
                yield readConsecutiveLines reader id ]
        sw.Stop()
        printfn "Stream read %d in %d ms" result.Length sw.ElapsedMilliseconds

    readFromStream()

What might be the problems with this approach?

1. Blocking the current thread

This is a synchronous use of reading from a Stream. In fact, `StreamReader` can only be used in a synchronous fashion. There are no methods that even offer the Asynchronous Programming Model (APM). For that, you'll need to read from the `Stream` in chunks and find the line breaks yourself. We'll get to more on this in a few minutes.

2. Poor composition

First off, the sample above is performing side effects throughout, and side-effects don't compose. You might suggest using standard function composition to create a complete message parser, but what if you don't know ahead of time what type of request body you'll receive? Can you pause processing to select the appropriate processor? You could certainly address this with some conditional branching, but where exactly do you tie that into your current logic? Also, the current parser breaks on lines using the StreamReader's built in logic. If you want to do parsing on individual lines, where would you tie in that logic?

3. Memory consumption

In this example, we are not taking any care about our memory use. To that end, you might argue that we should just use `StreamReader.ReadToEnd()` and then break up the string using a compositional parser like [FParsec](http://http://www.quanttec.com/fparsec/). If we don't care about careful use of memory, that's actually quite a good idea. However, in most network I/O situations -- such as writing high-performance servers -- you really want to control your memory use very carefully. `StreamReader` does allow you to specify the chunk size, so it's not a complete case against using `StreamReader`, which is why this is a last-place argument. And of course, you can certainly go after `Stream.Read()` directly.

## IEnumerable-based processing

How can we add better compositionality and refrain from blocking the thread? One way others have solved this problem is through the use of iterators. A common example can be found on [Stack Overflow](http://stackoverflow.com/questions/2630359/convert-stream-to-ienumerable-if-possible-when-keeping-laziness). Iterators allow you to publish a lazy stream of data, either one element at a time or in chunks, and through LINQ or F#'s `Seq` module, chain together processors for the data.

    // Read the stream byte by byte
    let readStreamByByte (stream: System.IO.Stream) =
        seq { while true do
                let x = stream.ReadByte()
                if (int x) < 0 then ()
                else yield x }

    // Read the stream by chunks
    let readStreamByChunk chunkSize (stream: System.IO.Stream) =
        let buffer = Array.zeroCreate<byte> chunkSize
        seq { while true do
                let bytesRead = stream.Read(buffer, 0, chunkSize)
                if bytesRead = 0 then ()
                else yield buffer }

    // When you are sure you want text by lines
    let readStreamByLines (reader: System.IO.StreamReader) =
        seq { while not reader.EndOfStream do
                yield reader.ReadLine() }

Three options are presented. In each, I'm using a `Stream` or `StreamReader`, but you could just as easily replace those with a `byte[]`, `ArraySegment<byte>`, or `SocketAsyncEventArgs`. What could be wrong with these options?

1. Lazy, therefore resource contentious

Iterators are pull-based, so you'll only retrieve a chunk when requested. This sounds pretty good for your processing code, but it's not a very good situation for your sockets. If you have data coming in, you want to get it moved out as quickly as possible to free up your allocated threads and pinned memory buffers for more incoming or outgoing traffic.

2. Lazy, therefore non-deterministic

Each of these items is lazy; therefore, it's impossible to know when you can free up resources. Who owns the `Stream` or `StreamReader` passed into each method? When is it safe to free the resource? If used immediately within a function, you may be fine, but you'd also be just as well off if you used a `list` or `array` comprehension and blocked until all bytes were read. (The one possible exception might be when using a [co-routine style async pattern](http://tomasp.net/blog/csharp-async.aspx).)

## A fold, by any other name

Looking back to our example of parsing the headers of an HTTP request message, we most likely want to return not just a set of strings but an abstract syntax tree represented as an F# discriminated union. A perfect candidate for taking in our iterator above and producing the desired result is our old friend [`Seq.fold`](http://msdn.microsoft.com/en-us/library/ee353471.aspx).

    val fold : ('State -> 'a -> 'State) -> 'State -> seq<'a> -> 'State

The left fold is a very useful function. It equivalent to the [`Enumerable.Aggregate`](http://msdn.microsoft.com/en-us/library/system.linq.enumerable.aggregate.aspx) extension method in LINQ. This function takes in allows for the incremental creation of a result starting with a seed value.

Looks like we're done here. However, there are still problems with stopping here:

1. Composition

You can certainly use function composition to generate an appropriate state incrementing function, but you would still have the problem of being able to pause to delay selecting the appropriate message body processor.

2. Early termination

Suppose you really only ever want just the message headers. How would you stop processing to free your resources as quickly as possible? Forget it. Fold is going to keep going until it runs out of chunks. Even if you have your fold function stop updating the state after the headers are complete, you won't get a result until the entire data stream has been processed.

Finally, we still haven't addressed the original issues with iterators.

## Iteratees

The iteratee itself is a data consumer. It consumes data in chunks until it has either consumed enough data to produce a result or receives an EOF.

An iteratee is based on the `fold` operator with two differences:

1. The iteratee may complete its processing early by returning a Done state. The iteratee may return the unused portion of any chunk it was passed in its Done state. This should not be confused with the rest of the stream not yet provided by the enumerator.

2. The iteratee does not pull data but receives chunks of data from an "enumerator". It returns a continuation to receive and process the next chunk. This means that an iteratee may be paused at any time, either by neglecting to pass it any additional data or by passing an Empty chunk.

*)
module Iteratee =
    open FSharpx
    open FSharpx.Monoid
    
    /// A stream of chunks of data generated by an Enumerator.
    /// The stream can be composed of chunks of 'a, empty blocks indicating a wait, or an EOF marker.
    /// Be aware that when using #seq<_> types, you will need to check for both Seq.empty ([]) and Empty.
    type Stream<'a when 'a : comparison> =
        | Chunk of ArraySegment<'a>
        | Empty
        | EOF
        with
        static member op_Nil() = Empty
        static member op_Append(a, b) =
            match a with
            | Chunk a' ->
                match b with
                | Chunk b' -> Chunk <| ArraySegment.append a' b'
                | _ -> a
            | _ -> b

    module Stream =
        let isEmpty = function
            | Empty -> true
            | Chunk s when ArraySegment.isEmpty s -> true
            | _ -> false
    
    /// The iteratee is a stream consumer that will consume a stream of data until either 
    /// it receives an EOF or meets its own requirements for consuming data. The iteratee
    /// will return Continue whenever it is ready to receive the next chunk. An iteratee
    /// is fed data by an Enumerator, which generates a Stream. 
    type Iteratee<'el,'a when 'el : comparison> = Stream<'el> -> IterResult<'el,'a>
    and IterResult<'el,'a when 'el : comparison> =
        | Done of 'a * Stream<'el>
        | Error of exn * 'a option * Stream<'el> option
        | Continue of Iteratee<'el,'a>
    
    let returnI a : Iteratee<_,_>  = fun s -> Done(a,s)
    let empty<'a when 'a : comparison> : Iteratee<'a,_> = returnI ()
    let throwI e  : Iteratee<_,_>  = fun s -> Error(e, None, Some s)

    // TODO: re-apply fix
    let rec bind (f:'a -> Iteratee<'b,'c>) (m:Iteratee<'b,'a>) : Iteratee<'b,'c> =
        fun s ->
            match m s with
            | Done(a,s')    -> (f a) s'
            | Error(e,_,s') -> Error(e,None,s')
            | Continue(i)   -> Continue(bind f i)

    let isIterActive = function
        | Continue _ -> true
        | _          -> false

    let rec run m =
        match m EOF with
        | Done(a,_)    -> a
        | Continue i   -> run i
        | Error(e,_,_) -> raise e

    let rec runR = function
        | Done(a,_)    -> Done(a, Empty)
        | Continue i   -> runR <| i EOF
        | Error(e,a,s) -> Error(e,a,Option.map (fun _ -> Empty) s) 
        
    let stepR r f notActive =
        match r with
        | Continue i -> Continue <| fun s -> f (i s)
        | _ -> notActive

    let onDoneR f r =
        let rec check r =
            stepR r check (f r)
        check r

    let onDone f (i:Iteratee<_,_>) : Iteratee<_,_> = fun s -> onDoneR f (i s)

    let fmapR f = function
        | Done(a,s)    -> Done(f a, s)
        | Error(e,a,s) -> Error(e, (Option.map f a), s)
        | _            -> failwith "fmapR"

    let fmapI f (i:Iteratee<_,_>) = onDone (fmapR f) i

    let getResidual = function
        | Done(_,s)    -> s
        | Error(_,_,s) -> defaultArg s Empty
        | _            -> failwith "getResidual"

    let setResidual = function
        | Done(a,_)    -> fun s -> Done(a,s)
        | Error(e,a,_) -> fun s -> Error(e,a,Some s)
        | _            -> failwith "setResidual"

    let genCatchI (i:Iteratee<_,_>) handler conv =
        let check r =
            match r with
            | Done(a,s)         -> Done(conv a, s)
            | Error(e,_,_) as r -> (handler e (setResidual r Empty)) (getResidual r)
            | _ -> failwith "genCatchI"
        onDone check i

    let either (left: _ -> Iteratee<_,_>) (right: _ -> Iteratee<_,_>) =
        fun (e: Choice<_,_>) ->
            match e with
            | Choice1Of2 a -> left a
            | Choice2Of2 b -> right b
       
    let catchI i handler = genCatchI i handler id

    let tryI i = genCatchI i (curry (returnI << Choice2Of2)) Choice1Of2

    let tryRI r =
        let check = function
            | Error(e,a,s) -> Done(Choice2Of2 <| Error(e,a,Some Empty), defaultArg s Empty)
            | _ as r       -> fmapR Choice1Of2 r
        onDone check r

    let tryEOFI i =
        let check = function
            | Error(e, _, s) when (e :? System.IO.EndOfStreamException) -> Done(None, defaultArg s Empty)
            | _ as r -> fmapR Some r
        onDone check i

    let tryFI i =
        let check = function
            | Error(e,_,s) -> Done(Choice2Of2 e, defaultArg s Empty)
            | _ as r       -> fmapR Choice1Of2 r
        onDone check i

    let runIterR r = fun s ->
        match s with
        | Empty -> r
        | _ ->
            match r with
            | Done(a,s')    -> Done(a, mappend s s')
            | Continue i    -> i s
            | Error(e,a,s') -> Error(e,a, Option.map (mappend s) s')

    let reRunIter = function
        | Continue i -> i
        | _ as r     -> runIterR r

    let runI (i: Iteratee<_,_>) : Iteratee<_,_> = runIterR <| runR (i EOF)

    let tryCatch handler m = catchI m <| fun e _ -> handler e

    let tryFinally compensation (m:Iteratee<_,_>) : Iteratee<_,_> =
        let rec step m = fun s ->
            match m s with
            | Continue i -> Continue <| step i
            | m          -> compensation(); m
        in step m
        
    type IterateeBuilder() =
        member this.Return(x) = returnI x
        member this.ReturnFrom(m:Iteratee<_,_>) = m
        member this.Bind(m, f) = bind f m
        member this.Zero() = empty<_>
        member this.Combine(comp1, comp2) = bind (fun () -> comp2) comp1
        member this.Delay(f) = bind f empty<_>
        member this.TryCatch(m, handler) = tryCatch handler m
        member this.TryFinally(m, compensation) = tryFinally compensation m
        member this.Using(res:#System.IDisposable, body) =
            this.TryFinally(body res, (fun () -> match res with null -> () | disp -> disp.Dispose()))
        member this.While(guard, m) =
            if not(guard()) then this.Zero() else
                this.Bind(m, (fun () -> this.While(guard, m)))
        member this.For(sequence:#seq<_>, body) =
            this.Using(sequence.GetEnumerator(),
                (fun enum -> this.While(enum.MoveNext, this.Delay(fun () -> body enum.Current))))
    let iteratee = IterateeBuilder()
    
    let inline returnM x = returnI x
    let inline (>>=) m f = bind f m
    let inline (=<<) f m = bind f m
    /// Sequential application
    let inline (<*>) f m = f >>= fun f' -> m >>= fun m' -> returnM (f' m')
    /// Sequential application
    let inline ap m f = f <*> m
    let inline map f m = m >>= fun x -> returnM (f x)
    let inline (<!>) f m = map f m
    let inline lift2 f a b = returnM f <*> a <*> b
    /// Sequence actions, discarding the value of the first argument.
    let inline ( *>) x y = lift2 (fun _ z -> z) x y
    /// Sequence actions, discarding the value of the second argument.
    let inline ( <*) x y = lift2 (fun z _ -> z) x y
    /// Sequentially compose two iteratee actions, discarding any value produced by the first
    let inline (>>.) m f = m >>= (fun _ -> f)
    /// Left-to-right Kleisli composition
    let inline (>=>) f g = fun x -> f x >>= g
    /// Right-to-left Kleisli composition
    let inline (<=<) x = flip (>=>) x

    let finallyI iter cleanup = iteratee {
        let! er = tryRI iter
        do! cleanup
        return! either returnI reRunIter er }

    let onDoneInput f iter : Iteratee<_,_> =
        let rec next acc iter s =
            let rec check = function
                | Continue i -> Continue <| fun s' -> next (acc << (mappend s)) i s'
                | r          -> stepR r check <| f r (acc s)
            in check (iter s)
        fun s -> next id iter s

    let tryFBI iter =
        let check r s =
            match r,s with
            | Done(a,s), _    -> Done(Choice1Of2 a, s)
            | Error(e,_,_), s -> Done(Choice2Of2 e, s)
            | _, _            -> failwith "tryFBI"
        in onDoneInput check iter

module Inum =
    open FSharpx
    open Iteratee
    
    /// An enumeratee is an enumerator that produces an iteratee using another iteratee as input.
    /// Enumeratees can be used for tasks such as encoding or encrypting data.
    type Inum<'eli,'elo,'a when 'eli : comparison and 'elo : comparison> = Iteratee<'elo,'a> -> Iteratee<'eli, IterResult<'elo,'a>>

    /// An enumerator generates a stream of data and feeds an iteratee, returning a new iteratee.
    type Onum<'el,'a when 'el : comparison> = Inum<unit,'el,'a>

    let joinR (ir:IterResult<'eli,IterResult<'elo,'a>>) : IterResult<'eli,'a> =
        match ir with
        | Done(i,s) -> runIterR (runR i) s
        | Error(e,None,s) -> Error(e,None,s)
        | Error(e,Some i,s) ->
            flip onDoneR (runR i) <| fun r ->
                match r with
                | Done(a,_) -> Error(e,Some a, s)
                | Error(e',a,_) -> Error(e',a,s)
                | _ -> failwith "joinR"
        | _ -> failwith "joinR: not done"

    let leftAssociativePipe (outer:Inum<'eli,'elo,'a>) (inner:'i -> Iteratee<'elo,'a>) : 'i -> Iteratee<'eli,'a> =
        fun i -> onDone joinR <| outer (inner i)
    let inline (|.) outer inner = leftAssociativePipe outer inner

    let rightAssociativePipe (inum:Inum<'eli,'elo,'a>) (iter:Iteratee<'elo,'a>) : Iteratee<'eli,'a> =
        onDone joinR <| inum iter
    let inline (.|) inum iter = rightAssociativePipe inum iter

    // Run the onum on the iteratee, passing EOF at the end to immediately return the result.
    let runOnum (onum: Onum<_,_>) iter = run (onum .| iter)
    let inline (|*) onum iter = runOnum onum iter

    // Apply the onum to the iteratee, producing a new iteratee.
    // `applyOnum << onum` would match the definition of an
    // `Enumerator` in other iteratee libraries.
    let applyOnum (onum: Onum<_,_>) iter = runI (onum .| iter)
    let inline (.|*) onum iter = applyOnum onum iter

    // cat concatenates two inums over an iteratee and runs the second inum
    // regardless of whether or not the iteratee has been exhausted.
    let cat (a: Inum<'a,'b,'c>) (b: Inum<'a,'b,'c>) : Inum<'a,'b,'c> =
        fun iter -> tryRI (a iter) >>= either (b << reRunIter) reRunIter

    // Lazy cat concatenates two inums over an iteratee but only runs the second inum
    // if the iteratee has not been exhausted.
    let lazyCat (a: Inum<'a,'b,'c>) (b: Inum<'a,'b,'c>) : Inum<'a,'b,'c> = 
        let check r = if isIterActive r then b <| reRunIter r else returnI r in
        fun iter -> tryRI (a iter) >>= either check reRunIter

