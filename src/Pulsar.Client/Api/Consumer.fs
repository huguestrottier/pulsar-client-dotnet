﻿namespace Pulsar.Client.Api

open FSharp.Control.Tasks.V2.ContextInsensitive
open System.Threading.Tasks
open FSharp.UMX
open System.Collections.Concurrent
open System
open Pulsar.Client.Internal
open System.Runtime.CompilerServices
open Pulsar.Client.Common
open pulsar.proto
open Microsoft.Extensions.Logging

type ConsumerException(message) =
    inherit Exception(message)

type ConsumerStatus =
    | Normal
    | Terminated

type ConsumerState = {
    Connection: ConnectionState
    Status: ConsumerStatus
    WaitingChannel: AsyncReplyChannel<Message>
}

type Consumer private (consumerConfig: ConsumerConfiguration, lookup: BinaryLookupService) =

    let consumerId = Generators.getNextConsumerId()
    let queue = new ConcurrentQueue<Message>()
    let nullChannel = Unchecked.defaultof<AsyncReplyChannel<Message>>
    let mutable messageCounter = 0

    let mb = MailboxProcessor<ConsumerMessage>.Start(fun inbox ->

        let rec loop (state: ConsumerState) =
            async {
                let! msg = inbox.Receive()
                if (state.Status <> ConsumerStatus.Normal)
                then
                    failwith (sprintf "Consumer status: %A" state.Status)
                else
                    match msg with
                    | ConsumerMessage.Connect ((broker, mb), channel) ->
                        if state.Connection = NotConnected
                        then
                            let! connection = SocketManager.registerConsumer broker consumerConfig consumerId mb |> Async.AwaitTask
                            channel.Reply()
                            return! loop { state with Connection = Connected connection }
                        else
                            return! loop state
                    | ConsumerMessage.Reconnect mb ->
                        // TODO backoff
                        if state.Connection = NotConnected
                        then
                            try
                                let! broker = lookup.GetBroker(consumerConfig.Topic) |> Async.AwaitTask
                                let! connection = SocketManager.reconnectConsumer broker consumerId |> Async.AwaitTask
                                return! loop { state with Connection = Connected connection }
                            with
                            | ex ->
                                mb.Post(ConsumerMessage.Reconnect mb)
                                Log.Logger.LogError(ex, "Error reconnecting")
                                return! loop state
                        else
                            return! loop state
                    | ConsumerMessage.Disconnected (connection, mb) ->
                        if state.Connection = Connected connection
                        then
                            mb.Post(ConsumerMessage.Reconnect mb)
                            return! loop { state with Connection = NotConnected }
                        else
                            return! loop state
                    | ConsumerMessage.MessageRecieved x ->
                        if state.WaitingChannel = nullChannel
                        then
                            queue.Enqueue(x)
                            return! loop state
                        else
                            state.WaitingChannel.Reply(x)
                            return! loop { state with WaitingChannel = nullChannel }
                    | ConsumerMessage.GetMessage ch ->
                        match queue.TryDequeue() with
                        | true, msg ->
                            ch.Reply msg
                            return! loop state
                        | false, _ ->
                            return! loop { state with WaitingChannel = ch }
                    | ConsumerMessage.Ack (payload, channel) ->
                        match state.Connection with
                        | Connected conn ->
                            do! SocketManager.send (conn, payload)
                            channel.Reply()
                            return! loop state
                        | NotConnected ->
                            //TODO put message on schedule
                            return! loop state
                    | ConsumerMessage.ConsumerClosed mb ->
                        let! broker = lookup.GetBroker(consumerConfig.Topic) |> Async.AwaitTask
                        let! newConnection = SocketManager.registerConsumer broker consumerConfig consumerId mb |> Async.AwaitTask
                        return! loop { state with Connection = Connected newConnection }
                    | ConsumerMessage.ReachedEndOfTheTopic ->
                        return! loop { state with Status = Terminated }
            }
        loop { Connection = NotConnected; Status = Normal; WaitingChannel = nullChannel }
    )

    member this.ReceiveAsync() =
        task {
            match queue.TryDequeue() with
            | true, msg ->
                return msg
            | false, _ ->
                 return! mb.PostAndAsyncReply(GetMessage)
        }

    member this.AcknowledgeAsync (msg: Message) =
        task {
            let command = Commands.newAck consumerId msg.MessageId CommandAck.AckType.Individual
            do! mb.PostAndAsyncReply(fun channel -> Ack (command, channel))
            return! Task.FromResult()
        }

    member private __.InitInternal() =
        task {
            let! broker = lookup.GetBroker(consumerConfig.Topic)
            return! mb.PostAndAsyncReply(fun channel -> Connect ((broker, mb), channel))
        }

    static member Init(consumerConfig: ConsumerConfiguration, lookup: BinaryLookupService) =
        task {
            let consumer = Consumer(consumerConfig, lookup)
            do! consumer.InitInternal()
            return consumer
        }



