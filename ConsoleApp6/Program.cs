using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BankSimulationApp
{
    public class Account
    {
        public int Id { get; set; }
        public string AccountNumber { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
        public decimal Balance { get; set; }
    }

    public class BankTransaction
    {
        public int Id { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class BankDbContext
    {
        public List<Account> Accounts { get; set; } = new();
        public List<BankTransaction> Transactions { get; set; } = new();

        private List<Account>? _accountsBackup;
        private List<BankTransaction>? _transactionsBackup;
        private bool _inTransaction = false;

        public void BeginTransaction()
        {
            if (!_inTransaction)
            {
                _inTransaction = true;
                _accountsBackup = Accounts.Select(a => new Account { Id = a.Id, AccountNumber = a.AccountNumber, OwnerName = a.OwnerName, Balance = a.Balance }).ToList();
                _transactionsBackup = Transactions.Select(t => new BankTransaction { Id = t.Id, Description = t.Description, Amount = t.Amount }).ToList();
            }
        }

        public void Commit()
        {
            if (_inTransaction)
            {
                _inTransaction = false;
                _accountsBackup = null;
                _transactionsBackup = null;
            }
        }

        public void Rollback()
        {
            if (_inTransaction && _accountsBackup != null && _transactionsBackup != null)
            {
                Accounts = _accountsBackup;
                Transactions = _transactionsBackup;
                _inTransaction = false;
                _accountsBackup = null;
                _transactionsBackup = null;
            }
        }
    }

    public interface IRepository<T> where T : class
    {
        T? GetById(int id);
        IEnumerable<T> GetAll();
        void Add(T entity);
        void Update(T entity);
    }

    public class Repository<T> : IRepository<T> where T : class
    {
        protected readonly BankDbContext _context;

        public Repository(BankDbContext context)
        {
            _context = context;
        }

        public T? GetById(int id)
        {
            if (typeof(T) == typeof(Account))
            {
                return _context.Accounts.FirstOrDefault(a => a.Id == id) as T;
            }
            return null;
        }

        public IEnumerable<T> GetAll()
        {
            if (typeof(T) == typeof(Account))
            {
                return _context.Accounts as IEnumerable<T>;
            }
            return _context.Transactions as IEnumerable<T>;
        }

        public void Add(T entity)
        {
            if (entity is Account acc) _context.Accounts.Add(acc);
            else if (entity is BankTransaction tx) _context.Transactions.Add(tx);
        }

        public void Update(T entity) { }
    }

    public interface IUnitOfWork
    {
        IRepository<Account> Accounts { get; }
        IRepository<BankTransaction> Transactions { get; }
        void BeginTransaction();
        void Commit();
        void Rollback();
    }

    public class UnitOfWork : IUnitOfWork
    {
        private readonly BankDbContext _context;
        public IRepository<Account> Accounts { get; }
        public IRepository<BankTransaction> Transactions { get; }

        public UnitOfWork(BankDbContext context, bool dummy = true)
        {
            _context = context;
            Accounts = new Repository<Account>(_context);
            Transactions = new Repository<BankTransaction>(_context);
        }

        public void BeginTransaction() => _context.BeginTransaction();
        public void Commit() => _context.Commit();
        public void Rollback() => _context.Rollback();
    }

    public class BankService
    {
        private readonly IUnitOfWork _unitOfWork;

        public BankService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public void TransferMoney(int senderId, int receiverId, decimal amount)
        {
            _unitOfWork.BeginTransaction();

            try
            {
                var sender = _unitOfWork.Accounts.GetById(senderId);
                var receiver = _unitOfWork.Accounts.GetById(receiverId);
                var systemBank = _unitOfWork.Accounts.GetById(3);

                if (sender == null || receiver == null || systemBank == null)
                {
                    throw new Exception("One of the accounts could not be found in the system!");
                }

                if (sender.Balance < amount)
                {
                    throw new Exception("Sender has insufficient balance!");
                }

                sender.Balance -= amount;
                receiver.Balance += amount;

                decimal commission = 5.0m;
                if (sender.Balance < commission)
                {
                    throw new Exception("Insufficient balance for the commission fee!");
                }

                sender.Balance -= commission;
                systemBank.Balance += commission;

                _unitOfWork.Accounts.Update(sender);
                _unitOfWork.Accounts.Update(receiver);
                _unitOfWork.Accounts.Update(systemBank);

                _unitOfWork.Transactions.Add(new BankTransaction
                {
                    Id = _unitOfWork.Transactions.GetAll().Count() + 1,
                    Description = $"{sender.OwnerName} -> {receiver.OwnerName} transfer ({amount} TL)",
                    Amount = amount
                });

                _unitOfWork.Commit();
                Console.WriteLine("\n✅ Transfer successfully completed!");
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                Console.WriteLine($"\n❌ Transaction failed, Rollback executed! Error: {ex.Message}");
            }
        }

        public void ReceiveAutoIncome(int targetAccountId, decimal amount)
        {
            _unitOfWork.BeginTransaction();
            try
            {
                var targetAccount = _unitOfWork.Accounts.GetById(targetAccountId);
                if (targetAccount == null) throw new Exception("Target account not found!");

                targetAccount.Balance += amount;
                _unitOfWork.Accounts.Update(targetAccount);

                _unitOfWork.Commit();
                Console.WriteLine($"\n💰 [Auto Income] {amount} TL automatically added to {targetAccount.OwnerName}!");
            }
            catch
            {
                _unitOfWork.Rollback();
            }
        }
    }

    public class Program
    {
        public static async Task Main(string[] args)
        {
            var dbContext = new BankDbContext();
            IUnitOfWork unitOfWork = new UnitOfWork(dbContext, true);

            unitOfWork.Accounts.Add(new Account { Id = 1, AccountNumber = "TR1", OwnerName = "Sehle (You)", Balance = 2000.0m });
            unitOfWork.Accounts.Add(new Account { Id = 2, AccountNumber = "TR2", OwnerName = "Serra (Receiver)", Balance = 200.0m });
            unitOfWork.Accounts.Add(new Account { Id = 3, AccountNumber = "TR-SYSTEM", OwnerName = "System Bank", Balance = 0.0m });

            var bankService = new BankService(unitOfWork);

          
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(30000); 
                    bankService.ReceiveAutoIncome(1, 50.0m);
                }
            });

           
            bool exit = false;
            while (!exit)
            {
                Console.WriteLine("\n========================================");
                Console.WriteLine("          BANK SIMULATION MENU          ");
                Console.WriteLine("========================================");
                Console.WriteLine("1. View Balances");
                Console.WriteLine("2. Transfer Money to Serra");
                Console.WriteLine("3. Exit");
                Console.Write("Select an option (1-3): ");

                string? choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        Console.WriteLine("\n--- CURRENT BALANCES ---");
                        foreach (var acc in unitOfWork.Accounts.GetAll())
                        {
                            Console.WriteLine($"{acc.OwnerName}: {acc.Balance} TL");
                        }
                        break;

                    case "2":
                        Console.Write("\nEnter amount to transfer to Serra: ");
                        if (decimal.TryParse(Console.ReadLine(), out decimal transferAmount))
                        {
                            bankService.TransferMoney(1, 2, transferAmount);
                        }
                        else
                        {
                            Console.WriteLine("❌ Invalid amount entered!");
                        }
                        break;

                    case "3":
                        exit = true;
                        Console.WriteLine("Exiting application. Goodbye!");
                        break;

                    default:
                        Console.WriteLine("❌ Invalid choice, please try again.");
                        break;
                }
            }
        }
    }
}
