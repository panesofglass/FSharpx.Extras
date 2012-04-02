namespace FSharpx

open System
open FSharp.Control

type Pipe<'i, 'o, 'r> (f, ?cancellationToken) =
  let agent = new Agent<PipeMessage<'i, 'o, 'r>>(f, ?cancellationToken = cancellationToken)

  member x.Start() = agent.Start()

  interface IDisposable with
    member x.Dispose() =
      let comp = agent.PostAndAsyncReply Close
      let finish _ = (agent :> IDisposable).Dispose()
      Async.StartWithContinuations(comp, finish, finish, finish)

and PipeMessage<'i, 'o, 'r>
  = HaveOutput of 'o
  | NeedInput
  | Done       of 'i option * 'r
  | PipeM      of Async<Pipe<'i, 'o, 'r>> * Async<'r>
  | Close      of AsyncReplyChannel<unit>

type Source<'o> (f, ?cancellationToken) =
  inherit Pipe<unit, 'o, unit>(f, ?cancellationToken = cancellationToken)
  // interface IObservable<'o>
  // or interface IEnumerator<'o> as this will be pulled on by the connector
  // implementation will surely use AsyncSeq<'o>
  // the loop defined by this function should bracket any resources to call dispose on Close

type Sink<'i, 'r> (f, ?cancellationToken) =
  inherit Pipe<'i, unit, 'r>(f, ?cancellationToken = cancellationToken)
  // interface IObserver<'i>
  // also has a GetResult(), similar to Task<'i>
  // similar also to Iteratee<'i, 'r>

type Conduit<'i, 'o> (f, ?cancellationToken) =
  inherit Pipe<'i, 'o, unit>(f, ?cancellationToken = cancellationToken)
  // interface ISubject<'i, 'o>
  // important to note that a Conduit cannot produce a result

//type Connector<'i, 'o, 'r>
  //inherit Pipe<'i, 'o, 'r>(f, ?cancellationToken = cancellationToken, ?timeout = timeout)
  // pull from the source, then push to the sink.
  // would implement IObservable for the push and IEnumerable for the pull?
  // leverage AsyncSeq -> IObservable for the implementation
  // if `Source` exposes `IObservable` and `Sink` exposes `IObserver`,
  // then `connect` could simply be `Subscribe`.
  // what about accessing the result from the `Sink`?
  // what about a merge function for `Sink` -> `Sink`?
  // is `Sink` really an `IObserver`, or is it an `Aggregate` call?

// Rx already defines most of this in its Join Patterns: http://social.msdn.microsoft.com/Forums/en-US/rx/thread/3ca3a0d4-ac61-4325-9fa2-ed622830b518?prof=required
// The missing piece is composition of IObserver.
// I also wonder if something more like a composable Aggregate would be better.
// Joinads is another option that would work much better: http://tomasp.net/blog/joinads-join-calculus.aspx
module Pipe =
  let connect (source:Source<'o>) (sink:Sink<'i,'r>) (f: 'r -> Async<unit>) =
    new Pipe<'o,'i,'r>(fun inbox ->
      sink.Start()
      source.Start()

      // NOTE: Would this be simpler using Async.Merge? What about: https://github.com/tpetricek/FSharp.Joinads

      // Scan for NeedInput
      let rec awaitSink () =
        inbox.Scan(function
          | Close reply -> Some (close reply)
          | NeedInput ->
              // Pull from the source, then await the output.
              Some (awaitOutput())
          // This one needs more thought.
          | Done(i, r) -> Some (f r)
          | _ -> None
        )

      // Scan for HaveOutput
      and awaitOutput () =
        inbox.Scan(function
          | Close reply -> Some (close reply)
          | HaveOutput o ->
              // Send to the sink
              Some(awaitSink())
          | _ -> None
        )

      and close (reply: AsyncReplyChannel<_>) =
        reply.Reply()
        async.Zero()

      awaitSink()
    )
