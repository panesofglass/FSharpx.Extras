module FSharpx.Pipes

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

type SinkState<'r> =
  | Continue of (Data -> SinkState<'r>)
  | Done of 'r * BS

type SinkResult<'r> =
  | NeedInput
  | Result of 'r * BS

type private SinkMessage<'r> =
  | Post of Data * AsyncReplyChannel<SinkResult<'r>>
  | Shutdown

type Sink<'r>(k) =
  let _k = k
  let agent = Agent<SinkMessage<'r>>.Start(fun inbox ->
    let rec loop k = async {
      let! msg = inbox.Receive()
      match msg with
      | Post(data, channel) ->
          let state' = k data
          match state' with
          | Continue k' ->
              channel.Reply(NeedInput)
              return! loop k'
          | Done(x, bs) -> channel.Reply(Result(x, bs))
      | Shutdown -> ()
    }
    loop k
  )
  member x.AsyncPost data = agent.PostAndAsyncReply(fun channel -> Post(data, channel))
  member x.Post data = agent.PostAndReply(fun channel -> Post(data, channel))
  member x.Shutdown() = agent.Post Shutdown

let inline makeSink k = Sink<_>(k)

// If all Pipes use agents, these could be generalized to:
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
        let result = sink.Post(EOF ByteString.empty) // AsyncSeq doesn't allow us to optimize with data along with Nil.
        match result with
        | Result(value, leftover) ->
            sink.Shutdown()
            return value, AsyncSeq.singleton leftover
        | _ -> return Unchecked.defaultof<'r>, AsyncSeq.empty
    | Cons(h, source') ->
        let result = sink.Post(Chunk h)
        match result with
        | Result(value, leftover) ->
            let source'' = asyncSeq {
              if not (ByteString.isEmpty leftover) then
                yield leftover
              yield! source'
            }
            return value, source''
        | NeedInput -> return! loop source'
  }
  loop source

// This synch-sink alternative is slower than the agent-based version and about equivalent with the iteratee version.
//type Sink<'r> = Data -> SinkState<'r>
//let inline makeSink (k: Sink<_>) data = k data
//
//let connect source sink : Async<'r * Source> =
//  let rec loop (source: Source) (sink: Sink<_>) = async {
//    let! next = source
//    match next with
//    | AsyncSeqInner.Nil ->
//        // TODO: Close the source's resources.
//        // AsyncSeq doesn't allow us to optimize with data along with Nil.
//        let result = sink (EOF ByteString.empty)
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

let empty<'a> =
  let res = Unchecked.defaultof<'a>
  makeSink <| function Chunk x -> Done(res, x) | EOF x -> Done(res, x)

let fold step seed =
  let f = ByteString.fold step
  let rec loop acc = function
    | Chunk xs when ByteString.isEmpty xs -> Continue (loop acc)
    | Chunk xs -> Continue (loop (f acc xs))
    | EOF xs when ByteString.isEmpty xs -> Done(acc, xs)
    | EOF xs -> Done(f acc xs, ByteString.empty)
  makeSink <| loop seed

let length =
  let rec step n = function
    | Chunk xs when ByteString.isEmpty xs -> Continue (step n)
    | Chunk xs -> Continue (step (n + xs.Count))
    | EOF xs when ByteString.isEmpty xs -> Done(n, xs)
    | EOF xs -> Done(n + xs.Count, ByteString.empty)
  makeSink <| step 0

let peek =
  let rec step = function
    | Chunk xs when ByteString.isEmpty xs -> Continue step
    | Chunk xs -> Done(Some(ByteString.head xs), xs)
    | EOF xs -> Done(None, xs)
  makeSink step

let head =
  let rec step = function
    | Chunk xs when ByteString.isEmpty xs -> Continue step
    | Chunk xs -> Done(Some(ByteString.head xs), ByteString.tail xs)
    | EOF xs -> Done(None, xs)
  makeSink step

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
  if n <= 0 then empty<_> else makeSink <| step n

let private skipWithPredicate pred byteStringOp =
  let rec step = function
    | Chunk xs when ByteString.isEmpty xs -> Continue step
    | Chunk xs ->
      let xs' = byteStringOp pred xs in
      if ByteString.isEmpty xs' then Continue step else Done((), xs')
    // TODO: | EOF xs when ByteString.isEmpty xs ->
    | EOF xs -> Done((), xs)
  makeSink step

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
  if n <= 0 then empty<_> else makeSink <| step ByteString.empty n

let private takeWithPredicate (pred:'a -> bool) byteStringOp =
  let rec step before = function
    | Chunk str when ByteString.isEmpty str -> Continue <| step before
    | Chunk str ->
        match byteStringOp pred str with
        | str', extra when ByteString.isEmpty extra -> Continue <| step (ByteString.append before str')
        | str', extra -> Done(ByteString.append before str', extra)
    // TODO: | EOF xs when ByteString.isEmpty xs ->
    | EOF xs -> Done(before, xs)
  makeSink <| step ByteString.empty

let takeWhile pred = takeWithPredicate pred ByteString.span
let takeUntil pred = takeWithPredicate pred ByteString.split

let heads str =
  let rec loop count str =
    if ByteString.isEmpty str then Done(count, str)
    else Continue (step count str)
  and step count str = function
    | Chunk x when ByteString.isEmpty x -> loop count str
    | Chunk x ->
      let c, t = ByteString.head str, ByteString.tail str
      let c', t' = ByteString.head x, ByteString.tail x
      if c = c' then step (count + 1) t (Chunk t') 
      else Done(count, x)
    // TODO: | EOF xs when ByteString.isEmpty xs ->
    | EOF xs -> Done(count, xs)
  makeSink <| step 0 str

// TODO: This is where we start needing to combine sinks, though these could work by chaining connects.
//let bind sink f =
  // TODO: Define an agent that passes data to the first sink and then uses f to create the second sink once the first is Done.
  // TODO: It's possible that we could just reuse iteratee here. In fact, if we use the synchronous definition above, that's exactly the same.
  // TODO: Return a Sink<_>
  
//let many i =
//    let rec inner cont = i >>= check cont
//    and check cont bs =
//        if ByteString.isEmpty bs then
//            Done(cont [], Empty)
//        else inner <| fun tail -> cont <| bs::tail
//    inner id
//
//let skipNewline =
//    let crlf = BS"\r\n"B
//    let lf = BS"\n"B
//    heads crlf >>= fun n ->
//        if n = 0 then
//            heads lf
//        else Done(n, Empty)
//
//let readLine = 
//    let isNewline c = c = '\r'B || c = '\n'B
//    takeUntil isNewline
//
//let readLines =
//    let rec lines cont = readLine >>= fun bs -> skipNewline >>= check cont bs
//    and check cont bs count =
//        match bs, count with
//        | bs, 0 when ByteString.isEmpty bs -> Done(cont [], EOF)
//        | bs, 0 -> Done(cont [bs], EOF)
//        | _ -> lines <| fun tail -> cont <| bs::tail
//    lines id
