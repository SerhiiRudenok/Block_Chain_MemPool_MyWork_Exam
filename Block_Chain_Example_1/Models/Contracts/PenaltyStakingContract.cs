using Block_Chain_Example_1.Services;
using Mono.TextTemplating;

namespace Block_Chain_Example_1.Models.Contracts
{
    public class PenaltyStakingContract : ISmartContract
    {
        public string Address { get; set; }
        public decimal _rewardPerBlockPerToken { get; set; }
        public decimal _earlyPenaltyPercent { get; set; } // Наприклад, 0.2m = 20%
        public string? LastValidationError { get; private set; }

        private readonly int _lockPeriodInBlocks;
        private readonly Dictionary<string, decimal> _stakes = new();
        private readonly Dictionary<string, int> _stakeStartBlock = new();
        public int LockPeriodInBlocks => _lockPeriodInBlocks;

        public PenaltyStakingContract(string address, decimal rewardPerBlockPerToken, int lockPeriodInBlocks, decimal earlyPenaltyPercent)
        {
            Address = address;
            _rewardPerBlockPerToken = rewardPerBlockPerToken;
            _lockPeriodInBlocks = lockPeriodInBlocks;
            _earlyPenaltyPercent = earlyPenaltyPercent;
        }

        public string GetConfiguration()
        {
            return $"PenaltyStakingContract: RewardPerBlockPerToken = {_rewardPerBlockPerToken}, LockPeriodInBlocks = {_lockPeriodInBlocks}, EarlyPenaltyPercent = {_earlyPenaltyPercent * 100}%";
        }

        public int GetStakeStartBlock(string userAddress)
        {
            return _stakeStartBlock.TryGetValue(userAddress, out var block) ? block : -1;
        }

        public decimal GetStakeAmount(string userAddress)
        {
            return _stakes.TryGetValue(userAddress, out var amount) ? amount : 0m;
        }

        public IEnumerable<string> GetStakers() => _stakes.Keys;

        public Dictionary<string, decimal> GetAllStakes() => new(_stakes);

        public bool ValidateTransaction(BlockChainService chain, Transaction tx, int currentBlock)
        {
            bool isDeposit = string.Equals(tx.ToAddress, Address, StringComparison.OrdinalIgnoreCase);
            bool isWithdraw = string.Equals(tx.FromAddress, Address, StringComparison.OrdinalIgnoreCase);

            if (isDeposit)
                return HandleDeposit(tx, currentBlock);
            else if (isWithdraw)
                return HandleWithdraw(tx, currentBlock);

            return false;
        }

        private bool HandleDeposit(Transaction tx, int currentBlock)
        {
            var user = tx.FromAddress;
            if (!_stakes.TryGetValue(user, out var currentStake))
                currentStake = 0m;

            _stakes[user] = currentStake + tx.Amount;

            if (!_stakeStartBlock.ContainsKey(user))
                _stakeStartBlock[user] = currentBlock;

            return true;
        }

        private bool HandleWithdraw(Transaction tx, int currentBlock)   // обробка виведення зі стейкінгу з урахуванням штрафу
        {
            var user = tx.ToAddress;

            if (!_stakes.TryGetValue(user, out var principal) || principal <= 0)
            {
                LastValidationError = "Немає стейку для цієї адреси";
                return false;
            }

            if (!_stakeStartBlock.TryGetValue(user, out var startBlock))
            {
                LastValidationError = "Немає інформації про початок стейкінгу";
                return false;
            }

            var heldBlocks = currentBlock - startBlock;
            var fullReward = heldBlocks * _rewardPerBlockPerToken * principal;
            var fullWithdrawable = principal + fullReward;

            // Пропорція відносно повної суми
            var proportion = tx.Amount / fullWithdrawable;
            var principalToWithdraw = principal * proportion;
            var rewardToWithdraw = fullReward * proportion;

            // Пропорційний штраф
            var penalty = (heldBlocks < _lockPeriodInBlocks)
                ? principalToWithdraw * _earlyPenaltyPercent
                : 0m;

            var actualPayout = principalToWithdraw + rewardToWithdraw - penalty;

            if (actualPayout <= 0)
            {
                LastValidationError = "Після застосування штрафу сума до виводу дорівнює нулю.";
                return false;
            }

            tx.Amount = actualPayout;

            // Оновлюємо залишок
            _stakes[user] = principal - principalToWithdraw;

            if (_stakes[user] <= 0.0000001m)
            {
                _stakes[user] = 0m;
                _stakeStartBlock.Remove(user);
            }

            LastValidationError = null;
            return true;
        }

        public decimal GetStakeInfo(string userAddress, int currentBlock) // повертає поточний стейк та нагороду
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

        public decimal GetPenalty(string userAddress, int currentBlock) // повертає суму штрафу при виводі
        {
            decimal principal = GetStakeAmount(userAddress);
            int startBlock = GetStakeStartBlock(userAddress);
            if (principal <= 0 || startBlock < 0)
                return 0m;

            int heldBlocks = currentBlock - startBlock;
            return heldBlocks < _lockPeriodInBlocks ? principal * _earlyPenaltyPercent : 0m;
        }

        public decimal GetWithdrawableAmount(string userAddress, int currentBlock)
        {
            var principal = GetStakeAmount(userAddress);
            var startBlock = GetStakeStartBlock(userAddress);
            if (principal <= 0 || startBlock < 0)
                return 0m;

            var heldBlocks = currentBlock - startBlock;
            var reward = heldBlocks * _rewardPerBlockPerToken * principal;
            var penalty = heldBlocks < _lockPeriodInBlocks ? principal * _earlyPenaltyPercent : 0m;

            return principal + reward - penalty;
        }
    }
}