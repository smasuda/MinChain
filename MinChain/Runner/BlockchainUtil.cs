using System;
using System.Collections.Generic;
using static MessagePack.MessagePackSerializer;

namespace MinChain
{
    public static class BlockchainUtil
    {
        public static IEnumerable<Block> Ancestors(this Block block,
             Dictionary<ByteString, Block> blocks)
        {
            var hash = block.Id;
            while (!hash.IsNull())
            {
                if (!blocks.TryGetValue(hash, out block)) yield break;
                yield return block;

                hash = block.PreviousHash;
            }
        }

        //SM: find a block which a fork happened.
        public static Block LowestCommonAncestor(Block b1, Block b2,
            Dictionary<ByteString, Block> blocks)
        {
            var set = new HashSet<ByteString>();

            using (var e1 = b1.Ancestors(blocks).GetEnumerator())
            using (var e2 = b2.Ancestors(blocks).GetEnumerator())
            {
                bool f1, f2 = false;
                while ((f1 = e1.MoveNext()) || (f2 = e2.MoveNext()))
                {
                    if (f1 && !set.Add(e1.Current.Id)) return e1.Current;
                    if (f2 && !set.Add(e2.Current.Id)) return e2.Current;
                }
            }

            return null;
        }

        // SM: this isn't merkle tree
        // TODO: calculate merkle tree.

        private static byte[] RootHashTransactionIds(IList<ByteString> txIds)
        {
            const int HashLength = 32;


            var i = 0;
            var ids = new byte[txIds.Count * HashLength];
            foreach (var txId in txIds)
            {
                Buffer.BlockCopy(
                    txId.ToByteArray(), 0, ids, i++ * HashLength, HashLength);
            }

            return Hash.ComputeDoubleSHA256(ids);
        }
        public static byte[] CalculateMerkleRoot(List<Transaction> txs)
        {
            if (txs.Count == 0) return null;//shouldn't happen(?)

            var hashlist = txs.ConvertAll(tx => Hash.ComputeDoubleSHA256(Serialize(tx)));

            while(hashlist.Count > 1){
                if( hashlist.Count % 2  !=0){
                    hashlist.Add(hashlist[hashlist.Count - 1]);
                }

                List<byte[]> newHashList = new List<byte[]>();

                for (int i = 0; i < hashlist.Count; i+=2){
                    var first = hashlist[i];
                    var second = hashlist[i + 1];
                    //hashsize is 32byte (256bit), so this could be written as 64 instead, though.
                    var concat = new byte[first.Length + second.Length];
                    System.Buffer.BlockCopy(first, 0, concat,0, first.Length);
                    System.Buffer.BlockCopy(second, 0, concat, first.Length,second.Length);
                    var newroot = Hash.ComputeDoubleSHA256(concat);
                    newHashList.Add(newroot);
                }

                hashlist = newHashList;
            }
            return hashlist[0];

           
        }

        public static Block DeserializeBlock(byte[] data)
        {
            var block = Deserialize<Block>(data);
            block.Original = data;
            block.Id = ByteString.CopyFrom(ComputeBlockId(data));
            return block;
        }

        public static Transaction DeserializeTransaction(byte[] data)
        {
            var tx = Deserialize<Transaction>(data);
            tx.Original = data;
            tx.Id = ByteString.CopyFrom(ComputeTransactionId(data));
            return tx;
        }

        public static byte[] ComputeBlockId(byte[] data)
        {
            var block = Deserialize<Block>(data).Clone();
            block.TransactionIds = null;
            block.Transactions = null;
            var bytes = Serialize(block);
            return Hash.ComputeDoubleSHA256(bytes);
        }

        public static byte[] ComputeTransactionId(byte[] data)
        {
            return Hash.ComputeDoubleSHA256(data);
        }

        public static byte[] GetTransactionSignHash(byte[] data)
        {
            var tx = Deserialize<Transaction>(data).Clone();
            foreach (var inEntry in tx.InEntries)
            {
                inEntry.PublicKey = null;
                inEntry.Signature = null;
            }
            var bytes = Serialize(tx);
            return Hash.ComputeDoubleSHA256(bytes);
        }

        public static byte[] ToAddress(byte[] publicKey)
        {
            // NOTE: In Bitcoin, recipient address is computed by SHA256 +
            // RIPEMD160.
            return Hash.ComputeDoubleSHA256(publicKey);
        }
    }
}
