using Block_Chain_Example_1.Services;

namespace Block_Chain_Example_1.Models.Contracts
{
    public class AllowListContract : ISmartContract
    {
        public string Address { get; }
        public HashSet<string> AllowedSenders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? LastValidationError { get; private set; }

        public AllowListContract(string address, IEnumerable<string> allowedAddresses)
        {
            Address = address;
            foreach (var addr in allowedAddresses)
                AllowedSenders.Add(addr);
        }

        public bool ValidateTransaction(BlockChainService chain, Transaction tx, int currentBlock)
        {
            if (string.Equals(Address, tx.ToAddress, StringComparison.OrdinalIgnoreCase))
            {
                if (!AllowedSenders.Contains(tx.FromAddress))
                {
                    LastValidationError = $"Адреса {tx.FromAddress} не має дозволу надсилати транзакції до {Address}.";
                    return false;
                }
                LastValidationError = null;
                return true;
            }
            LastValidationError = null;
            return true;
        }

        public string GetConfiguration()
        {
            return $"AllowListContract: дозволені адреси = [{string.Join(", ", AllowedSenders)}]";
        }
    }
}
