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
open FSharpx.Pipes

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

Async.StartWithContinuations(connect (makeSource()) (length()), fst >> printfn "Result of length is %d", ignore, ignore)

// compare with iteratee version:
Async.StartWithContinuations(enumerateAsyncChunk (makeSource()) Iteratee.Binary.length, run >> printfn "Result of iteratee length: %d", ignore, ignore)

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
