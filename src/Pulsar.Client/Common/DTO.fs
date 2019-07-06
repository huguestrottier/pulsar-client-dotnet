﻿namespace Pulsar.Client.Common

open System.Net
open System
open pulsar.proto
open System.IO.Pipelines
open Pipelines.Sockets.Unofficial
open System.IO
open System.Threading.Tasks
open FSharp.UMX

type ChecksumType =
    | Crc32c
    | No

type PartitionedTopicMetadata =
    {
        Partitions: uint32
    }

type ProducerSuccess =
    {
        GeneratedProducerName: string
    }

type LookupTopicResult =
    {
        Proxy: bool
        BrokerServiceUrl : string
    }

type TopicsOfNamespace =
    {
        Topics : string list
    }

type SubscriptionType =
    | Exclusive = 0
    | Shared = 1
    | Failover = 2

type MessageId =
    {
        LedgerId: LedgerId
        EntryId: EntryId
        Partition: int
    }
    with static member FromMessageIdData(messageIdData: MessageIdData) =
            {
                LedgerId = %messageIdData.ledgerId
                EntryId = %messageIdData.entryId
                Partition = messageIdData.Partition
            }

type LogicalAddress = LogicalAddress of DnsEndPoint
type PhysicalAddress = PhysicalAddress of DnsEndPoint

type Broker =
    {
        LogicalAddress: LogicalAddress
        PhysicalAddress: PhysicalAddress
    }

type Message =
    {
        MessageId: MessageId
        Payload: byte[]
    }

type WriterStream = Stream
type SerializedPayload = WriterStream -> Task
type Connection = SocketConnection * WriterStream * Broker

type ConnectionState =
    | NotConnected
    | Connected of Connection

type ProducerMessage =
    | Connect of (Broker*MailboxProcessor<ProducerMessage>) * AsyncReplyChannel<unit>
    | Reconnect of MailboxProcessor<ProducerMessage>
    | Disconnected of Connection * MailboxProcessor<ProducerMessage>
    | ProducerClosed of MailboxProcessor<ProducerMessage>
    | SendReceipt of CommandSendReceipt
    | SendMessage of SerializedPayload * AsyncReplyChannel<unit>
    | SendError of CommandSendError

type ConsumerMessage =
    | Connect of (Broker*MailboxProcessor<ConsumerMessage>) * AsyncReplyChannel<unit>
    | Reconnect of MailboxProcessor<ConsumerMessage>
    | Disconnected of Connection * MailboxProcessor<ConsumerMessage>
    | ConsumerClosed of MailboxProcessor<ConsumerMessage>
    | ReachedEndOfTheTopic
    | MessageRecieved of Message
    | GetMessage of AsyncReplyChannel<Message>
    | Ack of SerializedPayload * AsyncReplyChannel<unit>