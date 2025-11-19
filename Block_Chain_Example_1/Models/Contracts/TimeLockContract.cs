using Block_Chain_Example_1.Services;

namespace Block_Chain_Example_1.Models.Contracts
{
    public class TimeLockContract : ISmartContract
    {
        public string Address { get; }
        public int UnlockBlockIndex { get; set; }
        public string? LastValidationError { get; private set; }

        public TimeLockContract(string address, int unlockBlockIndex)
        {
            Address = address;
            UnlockBlockIndex = unlockBlockIndex;
        }

        public bool ValidateTransaction(BlockChainService chain, Transaction tx, int currentBlock)
        {
            if(string.Equals(Address, tx.FromAddress, StringComparison.OrdinalIgnoreCase))
            { 
                if (currentBlock < UnlockBlockIndex)
                {
                    LastValidationError = $"Цей контракт заблокований до блоку {UnlockBlockIndex}. Поточний блок: {currentBlock}.";
                    return false;
                }
                LastValidationError = null;
                return true;
            }
            LastValidationError = null;
            return false;
        }

        public string GetConfiguration()    // метод для отримання конфігурації контракту у вигляді рядка
        {
            return $"TimeLockContract: UnlockBlockIndex = {UnlockBlockIndex}";
        }

    }
}
