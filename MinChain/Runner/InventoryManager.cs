using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static MinChain.InventoryMessageType;
using static MessagePack.MessagePackSerializer;
using Newtonsoft.Json;

namespace MinChain
{
    public class InventoryManager
    {
        public const int MaximumBlockSize = 1024 * 1024; // 1MB

        // Hastable of blocks which this node has.
        public Dictionary<ByteString, byte[]> Blocks { get; }
            = new Dictionary<ByteString, byte[]>();

        public Dictionary<ByteString, Transaction> MemoryPool { get; }
            = new Dictionary<ByteString, Transaction>();

        public ConnectionManager ConnectionManager { get; set; }
        public Executor Executor { get; set; }


        public InventoryManager(){
            //load existing file
        }

        //Called by connection manager.
        public Task HandleMessage(InventoryMessage message, int peerId)
        {
            switch (message.Type)
            {
                case Advertise: return HandleAdvertise(message, peerId);
                case Request: return HandleRequest(message, peerId);
                case Body: return HandleBody(message, peerId);
                default: return Task.CompletedTask;
            }
        }

        //If a node receives a block, it advertises the fact to the other nodes.
        //If you receive an advertise, check if you have the block or not.
        //If you don't have the block, request the block.
        //
        // "advertise message" contains ID and type(block or transaction)
        async Task HandleAdvertise(InventoryMessage message, int peerId)
        {
            // Data should not contain anything. (To prevent DDoS)
            if (!message.Data.IsNull()) throw new ArgumentException();

            var haveObject = message.IsBlock ?
                Blocks.ContainsKey(message.ObjectId) :
                MemoryPool.ContainsKey(message.ObjectId);
            if (haveObject) return;

            message.Type = Request;
            await ConnectionManager.SendAsync(message, peerId);
        }

        async Task HandleRequest(InventoryMessage message, int peerId)
        {
            // Data should not contain anything. (To prevent DDoS)
            if (!message.Data.IsNull()) throw new ArgumentException();

            byte[] data;
            if (message.IsBlock)
            {
                if (!Blocks.TryGetValue(message.ObjectId, out data)) return;
            }
            else
            {
                Transaction tx;
                if (!MemoryPool.TryGetValue(message.ObjectId, out tx)) return;
                data = tx.Original;
            }

            message.Type = Body;
            message.Data = data;
            await ConnectionManager.SendAsync(message, peerId);
        }

        async Task HandleBody(InventoryMessage message, int peerId)
        {
            // Data should not exceed the maximum size.
            var data = message.Data;
            if (data.Length > MaximumBlockSize) throw new ArgumentException();

            var id = message.IsBlock ?
                BlockchainUtil.ComputeBlockId(data) :
                Hash.ComputeDoubleSHA256(data);
            if (!ByteString.CopyFrom(id).Equals(message.ObjectId)) return;

            if (message.IsBlock)
            {
                lock (Blocks)
                {
                    if (Blocks.ContainsKey(message.ObjectId)) return;
                    Blocks.Add(message.ObjectId, data);
                }

                var block = BlockchainUtil.DeserializeBlock(data);
                var prevId = block.PreviousHash;
                if (!Blocks.ContainsKey(prevId))
                {
                    await ConnectionManager.SendAsync(new InventoryMessage
                    {
                        Type = Request,
                        IsBlock = true,
                        ObjectId = prevId,
                    }, peerId);
                }

                Executor.ProcessBlock(block);
            }
            else
            {
                if (MemoryPool.ContainsKey(message.ObjectId)) return;

                var tx = BlockchainUtil.DeserializeTransaction(data);

                // Ignore the coinbase transactions.
                if (tx.InEntries.Count == 0) return;

                lock (MemoryPool)
                {
                    if (MemoryPool.ContainsKey(message.ObjectId)) return;
                    MemoryPool.Add(message.ObjectId, tx);
                }
            }

            message.Type = Advertise;
            message.Data = null;

            //tell the others about the new block or transaction
            await ConnectionManager.BroadcastAsync(message, peerId);
        }

        public Block TryLoadBlock(ByteString id, byte[] data)
        {
            // Data should not exceed the maximum size.
            if (data.Length > MaximumBlockSize)
                throw new ArgumentException(nameof(data));

            // Integrity check.
            var computedId = BlockchainUtil.ComputeBlockId(data);
            if (!ByteString.CopyFrom(computedId).Equals(id))
                throw new ArgumentException(nameof(id));

            // Try to deserialize the data for format validity check.
            var block = BlockchainUtil.DeserializeBlock(data);

            lock (Blocks)
            {
                if (Blocks.ContainsKey(id)) return null;
                Blocks.Add(id, data);
            }

            // Schedule the block for execution.
            Executor.ProcessBlock(block);

            return block;
        }
    }

}
