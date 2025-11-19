using Block_Chain_Example_1.Services;

namespace Block_Chain_Example_1.Models.Contracts
{
    public interface ISmartContract
    {
        string Address { get; }

        bool ValidateTransaction(BlockChainService chain, Transaction tx, int currentBlock);
        string? LastValidationError { get; } // властивість для зберігання останньої помилки валідації
        string GetConfiguration(); // метод для отримання конфігурації контракту у вигляді рядка
        

    }
}
