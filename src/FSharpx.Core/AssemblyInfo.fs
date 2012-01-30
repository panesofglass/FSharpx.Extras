module FSharpx.Core.AssemblyInfo
#nowarn "49" // uppercase argument names
#nowarn "67" // this type test or downcast will always hold
#nowarn "66" // tis upast is unnecessary - the types are identical
#nowarn "58" // possible incorrect indentation..
#nowarn "57" // do not use create_DelegateEvent
#nowarn "51" // address-of operator can occur in the code
open System
open System.Reflection
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
exception ReturnException183c26a427ae489c8fd92ec21a0c9a59 of obj
exception ReturnNoneException183c26a427ae489c8fd92ec21a0c9a59

[<assembly: ComVisible (false)>]

[<assembly: CLSCompliant (false)>]

[<assembly: Guid ("1e95a279-c2a9-498b-bc72-6e7a0d6854ce")>]

[<assembly: AssemblyTitle ("FSharpx")>]

[<assembly: AssemblyDescription ("FSharpx is a library for the .NET platform implementing general functional constructs on top of the F# core library.")>]

[<assembly: AssemblyProduct ("FSharpx")>]

[<assembly: AssemblyVersion ("1.4.120130")>]

[<assembly: AssemblyFileVersion ("1.4.120130")>]

[<assembly: AssemblyDelaySign (false)>]

()
