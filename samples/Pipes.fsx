// ----------------------------------------------------------------------------
// F# async extensions (Pipes.fsx)
// (c) Ryan Riley, 2012, Available under Apache 2.0 license.
// ----------------------------------------------------------------------------

// This example demonstrates how to pipeline a source through a series of sinks.

#r @"..\build\FSharpx.Core.dll"
open FSharp.Control
open FSharpx
open FSharpx.Iteratee
open FSharpx.Iteratee.Binary

type Source = AsyncSeq<BS>

type Data =
  | Chunk of BS
  | EOF of BS // This allows for early termination notification, or to provide any leftover data on the final pass.
  with
  member x.Prepend(bs) =
    match x with
    | Chunk xs -> Chunk(ByteString.append bs xs)
    | EOF xs -> EOF(ByteString.append bs xs)

type SinkResult<'r> =
  | NeedInput
  | Result of 'r * BS
  | Fault of exn * 'r option * BS option

type SinkState<'r> =
  | Continue of (Data -> SinkState<'r>)
  | Done of 'r * BS
  | Error of exn * 'r option * BS option

type private SinkMessage<'r> =
  | Post of Data * AsyncReplyChannel<SinkResult<'r>>
  | Reset

type Sink<'r>(k) =
  let _k = k
  let agent = Agent<SinkMessage<'r>>.Start(fun inbox ->
    let rec loop k = async {
      let! msg = inbox.Receive()
      match msg with
      | Post(data, reply) ->
          let state' = k data
          match state' with
          | Continue k' ->
              reply.Reply(NeedInput)
              return! loop k'
          | Done(x, bs) -> reply.Reply(Result(x, bs))
          | Error(e, x, bs) -> reply.Reply(Fault(e, x, bs))
      | Reset -> return! loop _k
    }
    loop _k
  )
  member x.Post data = agent.PostAndAsyncReply(fun reply -> Post(data, reply))
  member x.Reset() = agent.Post Reset

let inline makeSink k = Sink<_>(k)

// These could almost be generalized to:
//type Pipe<'i,'o> = 'i -> Async<'o>
//type Source' = Pipe<unit, AsyncSeqInner<BS>>
//type Sink'<'r> = Pipe<Data, 'r>
//type Conduit = Pipe<Data, AsyncSeqInner<BS>>

let connect (source: Source) (sink: Sink<_>) : Async<'r * Source> =
  let rec loop source = async {
    let! next = source
    match next with
    | AsyncSeqInner.Nil ->
        // TODO: Close the source's resources.
        let! result = sink.Post(EOF ByteString.empty) // AsyncSeq doesn't allow us to optimize with data along with Nil.
        match result with
        // TODO: Close the sink's resources.
        | Result(value, leftover) -> return value, AsyncSeq.singleton leftover
        // TODO: Need to fail hard for a non-conforming or failing sink.
        | _ -> return Unchecked.defaultof<'r>, AsyncSeq.empty
    | Cons(h, source') ->
        let! result = sink.Post(Chunk h)
        match result with
        | NeedInput -> return! loop source'
        | Result(value, leftover) ->
            let source'' = asyncSeq {
              if not (ByteString.isEmpty leftover) then
                yield leftover
              yield! source'
            }
            return value, source''
        // TODO: Need to fail hard for a non-conforming or failing sink.
        | _ -> return Unchecked.defaultof<'r>, AsyncSeq.empty
  }
  loop source

// This synch-sink alternative is slower than the agent-based version and about equivalent with the iteratee version.
//type Sink<'r> = Data -> SinkState<'r>
//let inline makeSink (k: Sink<_>) data = k data
//
//let connect (source: Source) (sink: Data -> SinkState<_>) : Async<'r * Source> =
//  let rec loop source sink = async {
//    let! next = source
//    match next with
//    | AsyncSeqInner.Nil ->
//        // TODO: Close the source's resources.
//        let result = sink (EOF ByteString.empty) // AsyncSeq doesn't allow us to optimize with data along with Nil.
//        match result with
//        // TODO: Close the sink's resources.
//        | Done(value, leftover) -> return value, AsyncSeq.singleton leftover
//        // TODO: Need to fail hard for a non-conforming or failing sink.
//        | _ -> return Unchecked.defaultof<'r>, AsyncSeq.empty
//    | Cons(h, source') ->
//        let result = sink (Chunk h)
//        match result with
//        | Continue k -> return! loop source' k
//        | Done(value, leftover) ->
//            let source'' = asyncSeq {
//              if not (ByteString.isEmpty leftover) then
//                yield leftover
//              yield! source'
//            }
//            return value, source''
//        // TODO: Need to fail hard for a non-conforming or failing sink.
//        | _ -> return Unchecked.defaultof<'r>, AsyncSeq.empty
//  }
//  loop source sink


// Simulate reading an HTTP request in small chunks.
let makeSource() = asyncSeq {
  yield BS"GET /home "B
  yield BS"HTTP/1.1\r\n"B
  yield BS"Host: http"B
  yield BS"://wizards"B
  yield BS"ofsmart.ne"B
  yield BS"t\r\nAccept:"B
  yield BS"\ttext/html"B
  yield BS"\r\n\r\n"B
}

// This will allow us a fair performance comparison with iteratee.
// Note that this isn't a true Enumerator, as the result is now in an Async<_>.
let rec enumerateAsyncChunk aseq i = async {
  let! res = aseq
  match res with
  | AsyncSeqInner.Nil -> return i
  | Cons(s1, s2) -> 
      match i with
      | Iteratee.Done(_,_) -> return i
      | Iteratee.Continue k -> return! enumerateAsyncChunk s2 (k (Iteratee.Chunk s1))
      | _ -> return i
}

let empty<'a> () =
  let res = Unchecked.defaultof<'a>
  makeSink <| function Chunk x -> Done(res, x) | EOF x -> Done(res, x)

// Unlike the iteratee monad, we aren't building a computation.
let length () =
  let rec step n = function
      | Chunk x when ByteString.isEmpty x -> Continue (step n)
      | Chunk x -> Continue (step (n + x.Count))
      | EOF x as s when ByteString.isEmpty x -> Done(n, x)
      | EOF x as s -> Done(n + x.Count, ByteString.empty)
  makeSink <| step 0

Async.StartWithContinuations(connect (makeSource()) (length()), fst >> printfn "Result of length is %d", ignore, ignore)

// compare with iteratee version:
Async.StartWithContinuations(enumerateAsyncChunk (makeSource()) Iteratee.Binary.length, run >> printfn "Result of iteratee length: %d", ignore, ignore)

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
  if n <= 0 then empty () else makeSink <| step n

let comp =
  let source = makeSource()
  async {
    let! (_, source') = connect source (skip 10)
    return! connect source' (length()) }

Async.StartWithContinuations(comp, fst >> printfn "Result of comp is %d", ignore, ignore)

// compare with iteratee version:
let iterateeComp = iteratee {
  let! rest = Iteratee.Binary.drop 10
  return! Iteratee.Binary.length
}

// compare with iteratee version:
Async.StartWithContinuations(enumerateAsyncChunk (makeSource()) iterateeComp, run >> printfn "Result of iteratee comp: %d", ignore, ignore)
