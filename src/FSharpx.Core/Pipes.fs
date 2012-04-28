module FSharpx.Pipes
#nowarn "40"

open System.Collections.Generic
open FSharp.Control
open FSharpx

type Source = AsyncSeq<BS>

type Data =
  | Chunk of BS
  | EOF of BS // This allows for early termination notification, or to provide any leftover data on the final pass.
  with
  member x.Prepend(bs) =
    match x with
    | Chunk xs -> Chunk(ByteString.append bs xs)
    | EOF xs -> EOF(ByteString.append bs xs)
  member x.Value =
    match x with
    | Chunk xs -> xs
    | EOF xs -> xs

type SinkState<'r> =
  | Continue of (Data -> SinkState<'r>)
  | Done of 'r * BS

type SinkResult<'r> =
  | NeedInput
  | Result of 'r * BS

//type private SinkMessage<'r> =
//  | Post of Data * AsyncReplyChannel<SinkResult<'r>>
//  | Shutdown
//
//type Sink<'r>(k) =
//  let _k = k
//  let agent = Agent<SinkMessage<'r>>.Start(fun inbox ->
//    let rec loop k = async {
//      let! msg = inbox.Receive()
//      match msg with
//      | Post(data, channel) ->
//          let state' = k data
//          match state' with
//          | Continue k' ->
//              channel.Reply(NeedInput)
//              return! loop k'
//          | Done(x, bs) -> channel.Reply(Result(x, bs))
//      | Shutdown -> ()
//    }
//    loop k
//  )
//  member x.AsyncPost data = agent.PostAndAsyncReply(fun channel -> Post(data, channel))
//  member x.Post data = agent.PostAndReply(fun channel -> Post(data, channel))
//  member x.Shutdown() = agent.Post Shutdown
//
//let inline makeSink k = Sink<_>(k)
//
//// If all Pipes use agents, these could be generalized to:
////type Pipe<'i,'o> = 'i -> Async<'o>
////type Source' = Pipe<unit, AsyncSeqInner<BS>>
////type Sink'<'r> = Pipe<Data, 'r>
////type Conduit = Pipe<Data, AsyncSeqInner<BS>>
//
//let connect (source: Source) (sink: Sink<_>) : Async<'r * Source> =
//  let rec loop source = async {
//    let! next = source
//    match next with
//    | AsyncSeqInner.Nil ->
//        // TODO: Close the source's resources.
//        let result = sink.Post(EOF ByteString.empty) // AsyncSeq doesn't allow us to optimize with data along with Nil.
//        match result with
//        | Result(value, leftover) ->
//            sink.Shutdown()
//            return value, AsyncSeq.singleton leftover
//        | _ -> return Unchecked.defaultof<'r>, AsyncSeq.empty
//    | Cons(h, source') ->
//        let result = sink.Post(Chunk h)
//        match result with
//        | Result(value, leftover) ->
//            let source'' = asyncSeq {
//              if not (ByteString.isEmpty leftover) then
//                yield leftover
//              yield! source'
//            }
//            return value, source''
//        | NeedInput -> return! loop source'
//  }
//  loop source

//let connect sink (source: seq<BS>) : 'r * seq<BS> =
//  let rec loop (source: IEnumerator<BS>) (sink: Sink<_>) =
//    if not <| source.MoveNext() then
//      // TODO: Close the source's resources.
//      // AsyncSeq doesn't allow us to optimize with data along with Nil.
//      let result = sink (EOF ByteString.empty)
//      match result with
//      | Done(value, leftover) ->
//          // TODO: Close the sink's resources.
//          value, Seq.singleton leftover
//      // TODO: Need to fail hard for a non-conforming or failing sink.
//      | _ -> Unchecked.defaultof<'r>, Seq.empty
//    else
//      let next = source.Current
//      let result = sink (Chunk next)
//      match result with
//      | Done(value, leftover) ->
//          let source' = Enumerator.iter {
//            if not (ByteString.isEmpty leftover) then
//              yield leftover
//            yield! source
//          }
//          value, Enumerator.toSeq <| fun () -> source'
//      | Continue k -> loop source k
//  loop (source.GetEnumerator()) sink

let connect sink source : Async<'r * Source> =
  let rec loop sink (source: Source) = async {
    let! next = source
    match next with
    | AsyncSeqInner.Nil ->
        // TODO: Close the source's resources.
        // AsyncSeq doesn't allow us to optimize with data along with Nil.
        let result = sink (EOF ByteString.empty)
        match result with
        | Done(value, leftover) ->
            // TODO: Close the sink's resources.
            return value, AsyncSeq.singleton leftover
        // TODO: Need to fail hard for a non-conforming or failing sink.
        | _ -> return Unchecked.defaultof<'r>, AsyncSeq.empty
    | Cons(h, source') ->
        let result = sink (Chunk h)
        match result with
        | Done(value, leftover) ->
            let source'' = asyncSeq {
              if not (ByteString.isEmpty leftover) then
                yield leftover
              yield! source'
            }
            return value, source''
        | Continue k -> return! loop k source'
  }
  loop sink source

let sinkResult x = fun (data: Data) -> Done(x, data.Value)

// bind doesn't quite work.
// We need a way to chain asyncConnect calls together:
// let res1, src' = asyncConnect src snk1
// let res2, src'' = asyncConnect src' snk2
// etc.
// This looks similar in some ways to the state monad.
// It _is_ the state monad!
//
//open State
//let readLineEx : State<_,_> = state {
//  let! res = (connect readLine)
//  let! _ = (connect skipNewline)
//  return res 
//}
//
// let value, state = (asyncConnect snk) state
// That's a function (asyncConnect snk) that takes a state and returns a value and a state.
// fun s -> (a, s)
// So a monadic form of sinks is nothing more than applying the sink to asyncConnect and using the state monad.
// Actually, it's a StateT with Async.
//open State
//let bind2 snk f = state {
//  let! res = asyncConnect snk
//  return (asyncConnect <| f res)
//}

type AsyncState<'a, 's> = 's -> Async<'a * 's>

let returnM x = fun s -> async.Return(x, s)

let bind (m: AsyncState<'a,'b>) (k: 'a -> AsyncState<'c,'b>) =
  fun src -> async {
    let! res, src' = m src
    return! (k res) src'
  }

let combine m1 m2 = bind m1 <| fun () -> m2

type AsyncStateBuilder() =
  member this.Return(x) = returnM x
  member this.ReturnFrom(m) = m
  member this.Bind(m, k) = bind m k
  member this.Combine(m1, m2) = combine m1 m2
  member this.Zero() = this.Return()
  member this.Delay(f) = this.Bind(this.Return(), f)
let asyncState = AsyncStateBuilder()

type Sink<'r> = AsyncState<'r, Source>

module Sink =
  let sink = asyncState

  let empty<'a> =
    let res = Unchecked.defaultof<'a>
    connect <| function Chunk x -> Done(res, x) | EOF x -> Done(res, x)

  let fold step seed =
    let f = ByteString.fold step
    let rec loop acc = function
      | Chunk xs when ByteString.isEmpty xs -> Continue (loop acc)
      | Chunk xs -> Continue (loop (f acc xs))
      | EOF xs when ByteString.isEmpty xs -> Done(acc, xs)
      | EOF xs -> Done(f acc xs, ByteString.empty)
    connect <| loop seed

  let length : Sink<_> =
    let rec step n = function
      | Chunk xs when ByteString.isEmpty xs -> Continue (step n)
      | Chunk xs -> Continue (step (n + xs.Count))
      | EOF xs when ByteString.isEmpty xs -> Done(n, xs)
      | EOF xs -> Done(n + xs.Count, ByteString.empty)
    connect <| step 0

  let peek : Sink<_> =
    let rec step = function
    | Chunk xs when ByteString.isEmpty xs -> Continue step
    | Chunk xs -> Done(Some(ByteString.head xs), xs)
    | EOF xs -> Done(None, xs)
    connect step

  let head : Sink<_> = 
    let rec step = function
    | Chunk xs when ByteString.isEmpty xs -> Continue step
    | Chunk xs -> Done(Some(ByteString.head xs), ByteString.tail xs)
    | EOF xs -> Done(None, xs)
    connect step

  let skip n =
    let rec step n = function
      | Chunk x when ByteString.isEmpty x -> Continue <| step n
      | Chunk x ->
        if ByteString.length x < n then
          Continue <| step (n - (ByteString.length x))
        else finish n x
      | EOF x when ByteString.isEmpty x -> Done((), x)
      | EOF x -> finish n x
    and finish n x = let extra = ByteString.skip n x in Done((), extra)
    if n <= 0 then empty<_> else connect <| step n

  let private skipWithPredicate pred byteStringOp =
    let rec step = function
      | Chunk xs when ByteString.isEmpty xs -> Continue step
      | Chunk xs ->
        let xs' = byteStringOp pred xs in
        if ByteString.isEmpty xs' then Continue step else Done((), xs')
      // TODO: | EOF xs when ByteString.isEmpty xs ->
      | EOF xs -> Done((), xs)
    connect step

  let skipWhile pred = skipWithPredicate pred ByteString.skipWhile
  let skipUntil pred = skipWithPredicate pred ByteString.skipUntil

  let take n =
    let rec step before n = function
      | Chunk str when ByteString.isEmpty str -> Continue <| step before n
      | Chunk str ->
        if ByteString.length str < n then
          Continue <| step (ByteString.append before str) (n - (ByteString.length str))
        else finish before n str
      | EOF str when ByteString.isEmpty str -> Done(before, str)
      | EOF str -> finish before n str
    and finish before n str = let str', extra = ByteString.splitAt n str in Done(ByteString.append before str', extra)
    if n <= 0 then empty<_> else connect <| step ByteString.empty n

  let private takeWithPredicate (pred:'a -> bool) byteStringOp =
    let rec step before = function
      | Chunk str when ByteString.isEmpty str -> Continue <| step before
      | Chunk str ->
          match byteStringOp pred str with
          | str', extra when ByteString.isEmpty extra -> Continue <| step (ByteString.append before str')
          | str', extra -> Done(ByteString.append before str', extra)
      // TODO: | EOF xs when not <| ByteString.isEmpty xs ->
      | EOF xs -> Done(before, xs)
    connect <| step ByteString.empty

  let takeWhile pred = takeWithPredicate pred ByteString.span
  let takeUntil pred = takeWithPredicate pred ByteString.split

  let heads str =
    let rec loop count str =
      if ByteString.isEmpty str then Done(count, str)
      else Continue <| step count str
    and step count str = function
      | Chunk x when ByteString.isEmpty x -> loop count str
      | Chunk x ->
        let c, t = ByteString.head str, ByteString.tail str
        let c', t' = ByteString.head x, ByteString.tail x
        if c = c' then step (count + 1) t (Chunk t') 
        else Done(count, x)
      // TODO: | EOF xs when ByteString.isEmpty xs ->
      | EOF xs -> Done(count, xs)
    connect <| step 0 str

//  let many i =
//    let rec inner cont = i >>= check cont
//    and check cont bs =
//      if ByteString.isEmpty bs then
//        result <| cont []
//      else inner <| fun tail -> cont <| bs::tail
//    inner id

  let skipNewline =
    let crlf = BS"\r\n"B
    let lf = BS"\n"B
    sink {
      let! count = heads crlf
      if count = 0 then
        return! heads lf
      else return count
    }

  let readLine =
    let isNewline c = c = '\r'B || c = '\n'B
    sink {
      let! res = takeUntil isNewline
      let! _ = skipNewline
      return res 
    }

  let rec readLines = sink {
    let! line = readLine
    if ByteString.isEmpty line then return [] else
    let! lines = readLines
    return line::lines
  }

//> let source = asyncSeq { yield BS"POST / HTTP/1.1\r\n"B; yield BS"Host: wizardsofsmart.net\r\n"B; yield BS"Connection: Close\r\n"B; yield BS"Cache-Control: None\r\n"B; yield BS"\r\n"B; yield BS"q=ebeneezer"B };;
//
//val source : AsyncSeq<BS>
//
//> let result, source' = Sink.readLines source |> Async.RunSynchronously;;
//
//val source' : Source
//val result : BS list = [<seq>; <seq>; <seq>; <seq>]
//
//> let value = result |> List.map ByteString.toString;;
//
//val value : string list =
//  ["POST / HTTP/1.1"; "Host: wizardsofsmart.net"; "Connection: Close";
//   "Cache-Control: None"]
