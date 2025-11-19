using Block_Chain_Example_1.Models;
using Block_Chain_Example_1.Models.Contracts;
using System.Security.Cryptography;
using System.Text;

namespace Block_Chain_Example_1.Services
{
    public class BlockChainService
    {
        public List<Block> Chain { get; set; } = new List<Block>();

        public int Difficulty { get; set; } = 1;

        private const int MaxTransactionPerBlock = 5;
        public string PrivateKeyXml { get; set; }
        public string PublicKeyXml { get; set; }
        public string StakingContractAdress { get; set; }
        public string PrivateKeyXmlContractWallet { get; set; }
        public string PublicKeyXmlContractWallet { get; set; }

        public Dictionary<string, Wallet> Wallets { get; set; } = new Dictionary<string, Wallet>();
        public List<Transaction> Mempool { get; set; } = new List<Transaction>();

        public Dictionary<string, ISmartContract> Contracts { get; } = new Dictionary<string, ISmartContract>(StringComparer.OrdinalIgnoreCase);

        // Халвінг
        public const decimal BaseMinerReward = 1.0m; // Початкова винагорода майнеру за блок
        private const int HalvingBlockInterval = 200; // Кожні 200 блоків відбувається халвінг

        //
        private const int TrgetBlockTimeSeconds = 20; // Середній час добування блоку в секундах: 1 блок на 20 секунд

        private const int AdjustEveryBlocks = 40; // 40 блоків перевіряти щоб коригувати складність

        private const double Tolerance = 0.2; // Допустиме відхилення від цільового часу на 20%


        public BlockChainService()
        {
            var rsa = RSA.Create();
            PrivateKeyXml = rsa.ToXmlString(true);
            PublicKeyXml = rsa.ToXmlString(false);

            var block = new Block(0, "", new DateTime(2025, 01, 01, 0, 0, 0, DateTimeKind.Utc));
            block.Mine(Difficulty);
            Chain.Add(block);

            var rsaContract = RSA.Create();
            PrivateKeyXmlContractWallet = rsaContract.ToXmlString(true);
            PublicKeyXmlContractWallet = rsaContract.ToXmlString(false);

            var stakingWallet = RegisterWallet(PublicKeyXmlContractWallet, "Penalty Staking Contract Wallet");
            StakingContractAdress = stakingWallet.Address;

            Contracts[StakingContractAdress] = new PenaltyStakingContract(
                StakingContractAdress,
                rewardPerBlockPerToken: 0.001m,     // винагорода за блок за кожен токен, що застейкано
                lockPeriodInBlocks: 20,             // період блокування в блоках
                earlyPenaltyPercent: 0.2m           // штраф за дострокове зняття (20%)
            );

        }

        private void AdjustDifficultyIfNeeded()          // Коригування складності майнінгу
        {
            if (Chain.Count % AdjustEveryBlocks != 0 || Chain.Count < AdjustEveryBlocks)
                return;

            var recent = Chain.Skip(1).TakeLast(AdjustEveryBlocks).ToList();

            if (recent.Count < AdjustEveryBlocks)
                return;

            var avgMs = recent.Average(b => b.MiningDurationMs);
            var targetMs = TrgetBlockTimeSeconds * 1000;

            var lowerBound = targetMs * (1 - Tolerance);
            var upperBound = targetMs * (1 + Tolerance);

            if (avgMs < lowerBound)
                Difficulty++;
            else if (avgMs > upperBound && Difficulty > 1)
                Difficulty--;

            if (Difficulty < 1)
                Difficulty = 1;

            if (Difficulty > 10)
                Difficulty = 10;
        }

        public Wallet RegisterWallet(string publicKeyXml, string displayName) // Реєстрація нового гаманця
        {
            var wallet = new Wallet
            {
                PublicKeyXml = publicKeyXml,
                Address = Wallet.DereveAddressFromPublicKeyXml(publicKeyXml),
                DisplayName = displayName,
            };
            Wallets[wallet.Address] = wallet;
            return wallet;
        }

        public decimal GetRewardForMiningBlock(string userAddress, int blockIndex) // Отримання винагороди за майнінг блоку для вказаної суми
        {
            return ((PenaltyStakingContract)Contracts[Contracts.Keys.First()]).GetStakeInfo(userAddress, blockIndex);
        }

        public void CreateTransaction(Transaction transaction)      // Створення нової транзакції
        {
            var rsa = RSA.Create();
            var wallet = Wallets[transaction.FromAddress];
            rsa.FromXmlString(wallet.PublicKeyXml);
            var payload = Encoding.UTF8.GetBytes(transaction.CanonicalPayload());
            var sig = Convert.FromBase64String(transaction.Signature);
            if (!rsa.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new Exception("Недійсний підпис транзакції"); // Якщо підпис недійсний, викидаю виключення


            //
            bool isFromContract = Contracts.ContainsKey(transaction.FromAddress);
            bool isCoinbase = string.Equals(transaction.FromAddress, "COINBASE", StringComparison.OrdinalIgnoreCase);

            if (!isFromContract && !isCoinbase) // Перевірка балансу лише для звичайних гаманців (не контрактів і не COINBASE)
            {
                var balances = GetBalances(true); // Отримую баланси всіх гаманців з урахуванням мемпулу
                if (!balances.TryGetValue(transaction.FromAddress, out var fromBal))
                    fromBal = 0;

                var requiredAmount = transaction.Amount + transaction.Fee;
                if (fromBal < requiredAmount)
                    throw new Exception($"Недостатньо коштів на гаманці {transaction.FromAddress}. Доступно: {fromBal}, потрібно: {requiredAmount}"); // Якщо недостатньо коштів, викидаю виключення
            }


            if (Contracts.TryGetValue(transaction.FromAddress, out var contractFrom)) // Перевірка смартконтракту відправника, якщо він існує
            {
                var res = contractFrom.ValidateTransaction(this, transaction, Chain.Count);
                if (res == false)
                {
                    throw new Exception(contractFrom.LastValidationError ?? "Транзакція відхилена контрактом.");
                }
            }
            
            if (Contracts.TryGetValue(transaction.ToAddress, out var contractTo)) // Перевірка смартконтракту отримувача, якщо він існує
            {
                var res = contractTo.ValidateTransaction(this, transaction, Chain.Count);
                if (res == false)
                {
                    return;
                    //throw new Exception(contractTo.LastValidationError ?? "Транзакція відхилена контрактом.");
                }
            }

            Mempool.Add(transaction);                                      // Додаю транзакцію до мемпулу
        }

        public Block MinePending(string privateKey)         // Майнінг нового блоку з транзакціями з мемпулу
        {
            var rsa = RSA.Create();
            rsa.FromXmlString(privateKey);
            var publicMinerKey = rsa.ToXmlString(false);
            var minerAdress = Wallets.Values.FirstOrDefault(w => w.PublicKeyXml == publicMinerKey)?.Address;

            var transaction = Mempool.OrderByDescending(t => t.Fee).Take(MaxTransactionPerBlock); // Вибираю транзакції з мемпулу з найбільшими комісіями
            decimal totalFee = transaction.Sum(t => t.Fee); // Обчислюю загальну комісію

            var PrevBlock = Chain[Chain.Count - 1];
            var newBlock = new Block(Chain.Count, PrevBlock.Hash);

            var minerReward = GetCurrentBlockReward(newBlock.Index);

            var baht = new List<Transaction>() {
                new Transaction()
                {
                    FromAddress = "COINBASE",
                        ToAddress = minerAdress,
                        Amount = minerReward + totalFee,
                }
            };
            baht.AddRange(transaction);

            newBlock.SetTransaction(baht);
            newBlock.Mine(Difficulty);

            AdjustDifficultyIfNeeded();

            newBlock.Sign(privateKey, publicMinerKey);
            Chain.Add(newBlock);

            Mempool = Mempool.Except(transaction).ToList(); // Видаляю додані транзакції з мемпулу

            return newBlock;
        }


        public bool IsValid()
        {
            for (int i = 1; i < Chain.Count; i++)                        // Починаю перевірку з другого блоку (індекс 1)
            {
                var current = Chain[i];
                var prev = Chain[i - 1];

                if (current.PrevHash != prev.Hash) return false;
                if (current.Hash != current.ComputeHash()) return false;
                if (!current.Verify()) return false;
                if (!current.HashValidProof()) return false;
            }
            return true;
        }


        public Dictionary<string, decimal> GetBalances(bool inclubeMempool = false) // Отримання балансів усіх гаманців
        {
            var balances = new Dictionary<string, decimal>();
            foreach (var block in Chain)
            {
                foreach (var tran in block.Transactions)
                {
                    ApplyTransactionToBalance(balances, tran);
                }

            }
            if (inclubeMempool)
            {
                foreach (var tran in Mempool)
                {
                    ApplyTransactionToBalance(balances, tran);
                }
            }
            return balances;
        }


        private static void ApplyTransactionToBalance(Dictionary<string, decimal> balances, Transaction tx) // Застосування транзакції до балансу гаманців
        {
            if (!balances.TryGetValue(tx.ToAddress, out var toBal))
                toBal = 0m;

            balances[tx.ToAddress] = toBal + tx.Amount;

            if (tx.FromAddress != "COINBASE")
            {
                if (!balances.TryGetValue(tx.FromAddress, out var fromBal))
                    fromBal = 0m;
                balances[tx.FromAddress] = fromBal - (tx.Amount + tx.Fee);
            }
        }

        public (Wallet wallet, string privateKeyXml) CreateWallet(string displayName)   // Створення нового гаманця з парою ключів
        {
            var rsa = RSA.Create();
            var privateKeyXml = rsa.ToXmlString(true);
            var publicKeyXml = rsa.ToXmlString(false);
            var wallet = RegisterWallet(publicKeyXml, displayName);
            return (wallet, privateKeyXml);
        }


        public static string SignPayload(string payload, string privateKeyXml)      // Підписати довільний текстовий рядок за допомогою приватного ключа у форматі XML
        {
            var rsa = RSA.Create();
            rsa.FromXmlString(privateKeyXml);
            var data = Encoding.UTF8.GetBytes(payload);
            var sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(sig);
        }


        public bool TryAddExternalBlock(List<Block> chain)        // Додаю новий блок до ланцюга блоків
        {
            if (!chain.Exists(x => x.Hash == Chain.First().Hash))
                return false;

            for (int i = 1; i < chain.Count - 1; i++)
            {
                if (chain[i].PrevHash != chain[i - 1].Hash)
                    return false;

                if (!chain[i].Verify())
                    return false;

                if (!chain[i].HashValidProof())
                    return false;

                if (chain[i].Hash != chain[i].ComputeHash())
                    return false;
            }

            if (chain.Count < Chain.Count)
                return false;

            var currentWork = ComputeTotalWork(Chain);
            var newWork = ComputeTotalWork(chain);
            if (newWork <= currentWork)
                return false;

            Chain.Clear();
            Chain.AddRange(chain);
            return true;
        }


        private static double ComputeTotalWork(List<Block> chain)   // Обчислення загальної роботи ланцюга блоків
        {
            double totalWork = 0;
            foreach (var block in chain)
            {
                totalWork += Math.Pow(2, block.Difficulty);
            }
            return totalWork;
        }


        public int? GetFirstInvalidIndex()      // Пошук індексу першого невалідного блоку
        {
            for (int i = 1; i < Chain.Count; i++)
            {
                var current = Chain[i];
                var prevBlock = Chain[i - 1];

                bool hashIsMatch = current.PrevHash == prevBlock.Hash;    // Перевірка відповідності хешів
                bool hashIsValid = current.Hash == current.ComputeHash(); // Перевірка валідності хешу
                bool signatureIsValid = current.Verify();                 // Перевірка валідності підпису

                if (!hashIsMatch || !hashIsValid || !signatureIsValid)
                    return i;
            }

            return null;
        }

        public Block FindBlock(string query)  // Пошук блоку за хешем або індексом
        {
            if (int.TryParse(query, out int index))
            {
                return Chain.FirstOrDefault(b => b.Index == index);
            }
            else
            {
                return Chain.FirstOrDefault(b => b.Hash.Equals(query, StringComparison.OrdinalIgnoreCase));
            }
        }

        public decimal GetCurrentBlockReward(int newBlockIndex)
        {
            if (newBlockIndex < 1) return 0m;

            int halvings = (newBlockIndex / HalvingBlockInterval);

            decimal reward = BaseMinerReward;

            for (int i = 0; i < halvings; i++)
            {
                reward /= 2;
            }

            return reward;


        }

    }
}

