﻿[<AutoOpen>]
module internal Pulsar.Client.Common.Tools

open System
open System.Collections
open System.Net
open System
open Microsoft.IO
open System.Runtime.ExceptionServices
open System.Collections.Generic

let MemoryStreamManager = RecyclableMemoryStreamManager()
let MagicNumber = int16 0x0e01
let RandomGenerator = Random()
let EmptyProps: IReadOnlyDictionary<string, string> = readOnlyDict []
let EmptyProperties: IReadOnlyDictionary<string, int64> = readOnlyDict []
let EmptyAckSet = BitArray(0)

let EmptyMetadata: IReadOnlyDictionary<string, string> = readOnlyDict []

// Converts

let int32ToBigEndian(num : Int32) =
    IPAddress.HostToNetworkOrder(num)

let int32FromBigEndian(num : Int32) =
    IPAddress.NetworkToHostOrder(num)
    
let int16ToBigEndian(num : Int16) =
    IPAddress.HostToNetworkOrder(num)
let int16FromBigEndian(num : Int16) =
    IPAddress.NetworkToHostOrder(num)

let int64ToBigEndian(num : Int64) =
    IPAddress.HostToNetworkOrder(num)

let int64FromBigEndian(num : Int64) =
    IPAddress.NetworkToHostOrder(num)

// Exception helper

let throwIf predicate createException arg =
    if predicate(arg) then
        raise(createException())
    else
        arg

let invalidArgIf predicate message =
    throwIf predicate (fun() -> ArgumentException(message))

let invalidArgIfBlankString =
    invalidArgIf (String.IsNullOrWhiteSpace)

let invalidArgIfNotGreaterThanZero =
    invalidArgIf ((>=) 0)

let invalidArgIfLessThanZero =
    invalidArgIf ((>) 0)

let invalidArgIfDefault msg =
    invalidArgIf (fun (arg) -> arg = Unchecked.defaultof<'a>) msg

let reraize<'a> ex =
    (ExceptionDispatchInfo.Capture ex).Throw()
    Unchecked.defaultof<'a>

let throwIfNotNull (exn:Exception) = if not(isNull exn) then raise exn

let (|Flatten|) (ex: exn) =
    match ex with
    | :? AggregateException as aggrEx -> aggrEx.Flatten().InnerException
    | _ -> ex

// DateTime conversions

let UTC_EPOCH = DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
let convertToMsTimestamp dateTime =
    let elapsed = dateTime - UTC_EPOCH
    elapsed.TotalMilliseconds |> int64

let convertToDateTime (msTimestamp: int64) =
    let ms = msTimestamp |> float
    UTC_EPOCH.AddMilliseconds ms

// Mix

let asyncDelay delay work =
    async {
        do! Async.Sleep delay
        work()
    } |> Async.StartImmediate
    
let asyncCancellableDelay delay work ct =
    Async.StartImmediate(async {
        do! Async.Sleep delay
        work()
    }, ct)

let signSafeMod dividend divisor =
    let modulo = dividend % divisor
    if modulo < 0
    then modulo + divisor
    else modulo
    
let toLongArray (bitSet: BitArray) =
    let resultArrayLengthLongs = bitSet.Length / 64 + (if bitSet.Length % 64 = 0 then 0 else 1)
    let resultArray = Array.create (resultArrayLengthLongs * 8) 0uy
    bitSet.CopyTo(resultArray, 0)
    [|
        for i in 0..8..resultArray.Length-1 do
            yield BitConverter.ToInt64(resultArray, i)
    |]
    
let fromLongArray (ackSets: int64[]) (numMessagesInBatch: int) =
    let bitArray = BitArray(numMessagesInBatch)
    let mutable index = 0
    for ackSet in ackSets do
        let stillToGo = numMessagesInBatch - index
        let currentLimit = if stillToGo > 64 then 64 else stillToGo 
        for bitNumber in 1..currentLimit do
            bitArray.[index] <- (ackSet &&& (1L <<< bitNumber-1)) <> 0L // https://stackoverflow.com/a/4854257/1780648
            index <- index + 1
    bitArray