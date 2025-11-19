using Block_Chain_Example_1.Services;

namespace Block_Chain_Example_1.Models.Contracts
{
    public class StakingContract : ISmartContract
    {
        public string Address { get; set; }

        public decimal _rewardPerBlockPerToken { get; set; }    // Винагорода за блок за кожен токен, що застейкано
        public string? LastValidationError { get; private set; }

        
        private readonly int _lockPeriodInBlocks;               // Період блокування в блоках

        private readonly Dictionary<string, decimal> _stakes = new Dictionary<string, decimal>(); // Інформація про стейкинг за адресами
        private readonly Dictionary<string, int> _stakeStartBlock = new Dictionary<string, int>(); // Блок, з якого починається стейкинг за адресами
        public Dictionary<string, decimal> GetAllStakes() => new(_stakes);
        public int LockPeriodInBlocks => _lockPeriodInBlocks;
        public IEnumerable<string> GetStakers() => _stakes.Keys;

        public StakingContract(string address, decimal rewardPerBlockPerToken, int lockPeriodInBlocks)
        {
            Address = address;
            _rewardPerBlockPerToken = rewardPerBlockPerToken;
            _lockPeriodInBlocks = lockPeriodInBlocks;
        }

        public string GetConfiguration()
        {
            return $"StakingContract: RewordPerBlockPerToken = {_rewardPerBlockPerToken}, LockPeriodInBlocks = {_lockPeriodInBlocks}";
        }

        public int GetStakeStartBlock(string userAddress)
        {
            return _stakeStartBlock.TryGetValue(userAddress, out var block) ? block : -1;
        }

        public decimal GetStakeAmount(string userAddress)
        {
            return _stakes.TryGetValue(userAddress, out var amount) ? amount : 0m;
        }

        public bool ValidateTransaction(BlockChainService chain, Transaction tx, int currentBlock)
        { 
            bool isDeposit = String.Equals(tx.ToAddress, Address, StringComparison.OrdinalIgnoreCase);
            bool isWithdraw = String.Equals(tx.FromAddress, Address, StringComparison.OrdinalIgnoreCase);

            if (isDeposit)
            {
                return HandleDeposit(tx, currentBlock);
            }
            else if (isWithdraw)
            {
                return HandleWithdraw(tx, currentBlock);
            }
            return false;
        }

        private bool HandleDeposit(Transaction tx, int currentBlock)
        {
            var user = tx.FromAddress;
            if (!_stakes.TryGetValue(user, out var currentStake))
            {
                currentStake = 0m;
            }

            _stakes[user] = currentStake + tx.Amount;
            
            if(!_stakeStartBlock.ContainsKey(user))
                _stakeStartBlock[user] = currentBlock;
            return true;
        }

        private bool HandleWithdraw(Transaction tx, int currentBlock)
        {
            var user = tx.ToAddress;
            if (!_stakes.TryGetValue(user, out var currentStake))
            {
                return false; // Немає стейку для цієї адреси
            }
            if(!_stakeStartBlock.TryGetValue(user, out var startBlock))
            {
                return false; // Немає інформації про початок стейкингу
            }
            if (currentBlock < startBlock + _lockPeriodInBlocks)
            {
                return false; // Період блокування ще не завершився
            }
            decimal rewards = (currentBlock - startBlock) * _rewardPerBlockPerToken * currentStake;
            decimal totalPayout = currentStake + rewards;
            if (tx.Amount > totalPayout)
            {
                return false; // Запит на зняття перевищує доступну суму
            }
            tx.Amount = totalPayout; // Оновлюємо суму транзакції з урахуванням винагороди
            _stakes[user] = 0m; // Обнуляємо стейк після зняття
            _stakeStartBlock.Remove(user); // Видаляємо інформацію про початок стейкингу
            return true;
        }

        public decimal GetStakeInfo(string userAddress, int currentBlock)
        {
            decimal currentStake = 0m;
            int startBlock = 0;
            if (_stakes.TryGetValue(userAddress, out var stake))
            {
                currentStake = stake;
            }
            if (_stakeStartBlock.TryGetValue(userAddress, out var sBlock))
            {
                startBlock = sBlock;
            }
            decimal reward = 0m;
            if (currentStake > 0 && startBlock > 0)
            {
                reward = (currentBlock - startBlock) * _rewardPerBlockPerToken * currentStake;
            }
            return reward;
        }
    }
}
