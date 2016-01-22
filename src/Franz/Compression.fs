﻿namespace Franz.Compression

open Franz
open Franz.Stream
open Snappy
open System.IO

/// Handles Snappy compression and decompression
[<AbstractClass; Sealed>]
type SnappyCompression private () =
    static let snappyCompressionFlag = 2y
    /// Encode message sets
    static member Encode(messageSets : MessageSet seq) =
        use stream = new MemoryStream()
        messageSets |> Seq.iter (fun x -> x.Serialize(stream))
        stream.Seek(0L, System.IO.SeekOrigin.Begin) |> ignore
        let buffer = Array.zeroCreate(int stream.Length)
        stream.Read(buffer, 0, int stream.Length) |> ignore
        [| MessageSet.Create(-1L, snappyCompressionFlag, null, SnappyCodec.Compress(buffer)) |]
    /// Decode a message set
    static member Decode(messageSet : MessageSet) =
        let message = messageSet.Message
        if message.CompressionCodec <> Snappy then invalidOp "This message is not compressed using Snappy"
        use stream = new MemoryStream(message.Value)
        use dest = new MemoryStream()

        // Currently Kafka uses a none standard Snappy format, the so called Xerial format.
        /// This format contains a specialized blocking format:
        ///
        /// |  Header  |  Block length  |  Block data  |  Block length  |  Block data  |
        /// | -------- | -------------- | ------------ | -------------- | ------------ |
        /// | 16 bytes |      int32     | snappy bytes |      int32     | snappy bytes |
        let handleXerialFormat() =
            stream.Seek(16L, SeekOrigin.Current) |> ignore
            while stream.Position < stream.Length do
                let blockSize = stream |> BigEndianReader.ReadInt32
                let buffer = Array.zeroCreate(blockSize)
                stream.Read(buffer, 0, blockSize) |> ignore
                let decompressedBuffer = SnappyCodec.Uncompress(buffer)
                dest.Write(decompressedBuffer, 0, decompressedBuffer.Length)

        handleXerialFormat()
        dest.Seek(0L, SeekOrigin.Begin) |> ignore
        let buffer = Array.zeroCreate(int dest.Length)
        dest.Read(buffer, 0, int dest.Length) |> ignore
        buffer |> MessageSet.Deserialize
