using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace vending_machine
{
    public readonly struct CurrencyAmount : IComparable<CurrencyAmount>, IEquatable<CurrencyAmount>
    {
        public int Kopeks { get; }

        private CurrencyAmount(int kopeks) => Kopeks = kopeks;

        public static CurrencyAmount FromRubles(decimal rubles)
        {
            var kop = (int)Math.Round(rubles * 100m, 0, MidpointRounding.AwayFromZero);
            return new CurrencyAmount(kop);
        }

        public static CurrencyAmount FromKopeks(int kop) => new CurrencyAmount(kop);

        public override string ToString()
        {
            var culture = new CultureInfo("ru-RU");
            var rub = Kopeks / 100m;
            return $"{rub.ToString("0.00", culture)} ₽";
        }

        public static CurrencyAmount operator +(CurrencyAmount a, CurrencyAmount b) => new(a.Kopeks + b.Kopeks);
        public static CurrencyAmount operator -(CurrencyAmount a, CurrencyAmount b) => new(a.Kopeks - b.Kopeks);
        public static bool operator >=(CurrencyAmount a, CurrencyAmount b) => a.Kopeks >= b.Kopeks;
        public static bool operator <=(CurrencyAmount a, CurrencyAmount b) => a.Kopeks <= b.Kopeks;
        public static bool operator >(CurrencyAmount a, CurrencyAmount b) => a.Kopeks > b.Kopeks;
        public static bool operator <(CurrencyAmount a, CurrencyAmount b) => a.Kopeks < b.Kopeks;

        public int CompareTo(CurrencyAmount other) => Kopeks.CompareTo(other.Kopeks);
        public bool Equals(CurrencyAmount other) => Kopeks == other.Kopeks;
        public override bool Equals(object? obj) => obj is CurrencyAmount other && Equals(other);
        public override int GetHashCode() => Kopeks;
    }

    public enum Denomination
    {
        R1 = 100,
        R2 = 200,
        R5 = 500,
        R10 = 1000
    }

    public sealed record Coin(Denomination Denom)
    {
        public int Kopeks => (int)Denom;
    }

    public interface IChangeStrategy
    {
        bool TryMakeChange(int amountKopeks, IDictionary<int, int> available, out Dictionary<int, int> change);
    }

    public sealed class GreedyChangeStrategy : IChangeStrategy
    {
        public bool TryMakeChange(int amountKopeks, IDictionary<int, int> available, out Dictionary<int, int> change)
        {
            change = new Dictionary<int, int>();
            if (amountKopeks < 0) return false;
            if (amountKopeks == 0) return true;

            foreach (var denom in available.Keys.OrderByDescending(x => x))
            {
                if (amountKopeks <= 0) break;

                var have = available[denom];
                if (have <= 0 || denom > amountKopeks) continue;

                var canUse = Math.Min(have, amountKopeks / denom);
                if (canUse > 0)
                {
                    change[denom] = canUse;
                    amountKopeks -= canUse * denom;
                }
            }

            if (amountKopeks == 0) return true;

            change.Clear();
            return false;
        }
    }
    public sealed class CashRegister
    {
        private readonly SortedDictionary<int, int> _vaultCoins = new();  
        private readonly SortedDictionary<int, int> _hopperCoins = new();
        private readonly IChangeStrategy _changeStrategy;

        public CashRegister(IChangeStrategy changeStrategy) => _changeStrategy = changeStrategy;

        private static void AddTo(IDictionary<int, int> dict, int denom, int count)
        {
            dict.TryGetValue(denom, out var cur);
            dict[denom] = cur + count;
        }

        private static int DeductFrom(IDictionary<int, int> dict, int denom, int count)
        {
            if (count <= 0) return 0;
            if (!dict.TryGetValue(denom, out var have) || have <= 0) return count;

            var use = Math.Min(have, count);
            var left = have - use;
            if (left == 0) dict.Remove(denom);
            else dict[denom] = left;

            return count - use; 
        }

        private Dictionary<int, int> MergeVaultAndHopperSnapshot()
        {
            var res = new Dictionary<int, int>(_vaultCoins);
            foreach (var kvp in _hopperCoins)
                AddTo(res, kvp.Key, kvp.Value);
            return res;
        }

        public int InsertedAmountKopeks => _hopperCoins.Sum(k => k.Key * k.Value);

        public int VaultBalanceKopeks => _vaultCoins.Sum(k => k.Key * k.Value);

        public void Insert(Coin coin) => AddTo(_hopperCoins, coin.Kopeks, 1);

        public Dictionary<int, int> RefundInserted()
        {
            var refund = new Dictionary<int, int>(_hopperCoins);
            _hopperCoins.Clear();
            return refund;
        }

        public void AddFloat(Denomination denom, int count)
        {
            if (count <= 0) return;
            AddTo(_vaultCoins, (int)denom, count);
        }

        public Dictionary<int, int> EmptyVault()
        {
            var all = new Dictionary<int, int>(_vaultCoins);
            _vaultCoins.Clear();
            return all;
        }

        public bool CanMakeChange(int amountKopeks)
        {
            var available = MergeVaultAndHopperSnapshot();
            return _changeStrategy.TryMakeChange(amountKopeks, available, out _);
        }

        public Dictionary<int, int> CommitPurchaseAndMakeChange(int changeAmountKopeks)
        {
            var availableSnapshot = MergeVaultAndHopperSnapshot();
            if (!_changeStrategy.TryMakeChange(changeAmountKopeks, availableSnapshot, out var changeMap))
                throw new InvalidOperationException("Сдачу посчитать не удалось, хотя её проверили ранее.");

            if (_hopperCoins.Count > 0)
            {
                foreach (var kvp in _hopperCoins.ToList())
                    AddTo(_vaultCoins, kvp.Key, kvp.Value);
                _hopperCoins.Clear();
            }

            var dispensed = new Dictionary<int, int>();
            foreach (var kvp in changeMap)
            {
                var denom = kvp.Key;
                var need = kvp.Value;
                var remain = DeductFrom(_vaultCoins, denom, need);
                remain = DeductFrom(_hopperCoins, denom, remain); 
                if (remain != 0)
                    throw new InvalidOperationException("Несогласованность содержимого кассы при выдаче сдачи.");
                dispensed[denom] = kvp.Value;
            }

            return dispensed;
        }

        public (Dictionary<int, int> vault, Dictionary<int, int> hopper) Snapshot()
            => (new Dictionary<int, int>(_vaultCoins), new Dictionary<int, int>(_hopperCoins));
    }


    public interface IProduct
    {
        string Id { get; }
        string Name { get; }
        CurrencyAmount Price { get; }
    }

    public abstract class Product : IProduct
    {
        public string Id { get; }
        public string Name { get; }
        public CurrencyAmount Price { get; }

        protected Product(string id, string name, CurrencyAmount price)
        {
            Id = id;
            Name = name;
            Price = price;
        }
    }

    public sealed class Drink : Product
    {
        public Drink(string id, string name, CurrencyAmount price) : base(id, name, price) { }
    }

    public sealed class Snack : Product
    {
        public Snack(string id, string name, CurrencyAmount price) : base(id, name, price) { }
    }

    public sealed class Inventory<T> where T : IProduct
    {
        public sealed class Line
        {
            public T Product { get; }
            public int Quantity { get; internal set; }
            public Line(T product, int quantity) { Product = product; Quantity = quantity; }
        }

        private readonly List<Line> _lines = new();

        public IEnumerable<Line> Items => _lines;

        public void AddNew(T product, int quantity)
        {
            if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (_lines.Any(l => l.Product.Id == product.Id))
                throw new InvalidOperationException("Товар с таким Id уже существует в инвентаре.");
            _lines.Add(new Line(product, quantity));
        }

        public Line? Find(string id) => _lines.FirstOrDefault(l => l.Product.Id == id);

        public bool TryDecrease(string id, int amount = 1)
        {
            var line = Find(id);
            if (line == null || line.Quantity < amount) return false;
            line.Quantity -= amount;
            return true;
        }

        public bool TryAddStock(string id, int amount)
        {
            var line = Find(id);
            if (line == null || amount <= 0) return false;
            line.Quantity += amount;
            return true;
        }
    }


    public readonly struct Result<T>
    {
        public bool IsSuccess { get; }
        public T? Value { get; }
        public string? Error { get; }

        private Result(bool ok, T? value, string? error) { IsSuccess = ok; Value = value; Error = error; }

        public static Result<T> Success(T value) => new(true, value, null);
        public static Result<T> Failure(string error) => new(false, default, error);
    }

    public sealed class Receipt
    {
        public IProduct Product { get; }
        public CurrencyAmount Price { get; }
        public CurrencyAmount Paid { get; }
        public CurrencyAmount ChangeAmount { get; }
        public Dictionary<int, int> Change { get; }

        public Receipt(IProduct product, CurrencyAmount price, CurrencyAmount paid, CurrencyAmount changeAmount, Dictionary<int, int> change)
        {
            Product = product;
            Price = price;
            Paid = paid;
            ChangeAmount = changeAmount;
            Change = change;
        }
    }


    public sealed class VendingMachine
    {
        private readonly Inventory<IProduct> _inventory;
        private readonly CashRegister _cash;
        private readonly string _adminPin;

        public VendingMachine(Inventory<IProduct> inventory, CashRegister cash, string adminPin)
        {
            _inventory = inventory;
            _cash = cash;
            _adminPin = adminPin;
        }

        public CurrencyAmount CurrentInserted => CurrencyAmount.FromKopeks(_cash.InsertedAmountKopeks);

        public void InsertCoin(Denomination denom) => _cash.Insert(new Coin(denom));

        public Dictionary<int, int> CancelAndRefund() => _cash.RefundInserted();

        public Result<AdminSession> TryEnterAdmin(string pin)
            => pin == _adminPin ? Result<AdminSession>.Success(new AdminSession(_inventory, _cash)) : Result<AdminSession>.Failure("Неверный PIN");

        public Result<Receipt> Buy(string productId)
        {
            var line = _inventory.Find(productId);
            if (line == null) return Result<Receipt>.Failure("Товар не найден.");
            if (line.Quantity <= 0) return Result<Receipt>.Failure("Товар закончился.");

            var price = line.Product.Price;
            var paid = CurrentInserted;

            if (paid < price) return Result<Receipt>.Failure("Недостаточно средств.");

            var changeKop = paid.Kopeks - price.Kopeks;

            if (!_cash.CanMakeChange(changeKop))
                return Result<Receipt>.Failure("Автомат не может выдать сдачу.");

            var change = _cash.CommitPurchaseAndMakeChange(changeKop);
            _inventory.TryDecrease(productId, 1);

            var receipt = new Receipt(line.Product, price, paid, CurrencyAmount.FromKopeks(changeKop), change);
            return Result<Receipt>.Success(receipt);
        }
    }

    public sealed class AdminSession
    {
        private readonly Inventory<IProduct> _inventory;
        private readonly CashRegister _cash;

        internal AdminSession(Inventory<IProduct> inventory, CashRegister cash)
        {
            _inventory = inventory;
            _cash = cash;
        }

        public IEnumerable<Inventory<IProduct>.Line> ProductsSnapshot() => _inventory.Items;

        public bool AddStock(string productId, int amount) => _inventory.TryAddStock(productId, amount);

        public void AddFloat(Denomination denom, int count) => _cash.AddFloat(denom, count);

        public Dictionary<int, int> CollectAllCash() => _cash.EmptyVault();

        public (Dictionary<int, int> vault, Dictionary<int, int> hopper) CashSnapshot() => _cash.Snapshot();

        public CurrencyAmount VaultBalance => CurrencyAmount.FromKopeks(_cash.VaultBalanceKopeks);
    }


    public static class App
    {
        public static void Run()
        {
            var inventory = new Inventory<IProduct>();
            inventory.AddNew(new Drink("D1", "Вода", CurrencyAmount.FromRubles(2m)), quantity: 10);
            inventory.AddNew(new Drink("D2", "Кола", CurrencyAmount.FromRubles(7m)), quantity: 8);
            inventory.AddNew(new Snack("S1", "Сникерс", CurrencyAmount.FromRubles(5m)), quantity: 5);

            var cash = new CashRegister(new GreedyChangeStrategy());
            SeedFloat(cash);

            var vm = new VendingMachine(inventory, cash, adminPin: "1234");

            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== ВЕНДИНГ-АВТОМАТ ===");
                Console.WriteLine("Внесено: " + vm.CurrentInserted);
                Console.WriteLine();
                PrintProducts(inventory);
                Console.WriteLine();
                Console.WriteLine("1) Вставить монету");
                Console.WriteLine("2) Купить товар");
                Console.WriteLine("3) Отмена и вернуть монеты");
                Console.WriteLine("9) Админ-режим");
                Console.WriteLine("0) Выход");
                Console.Write("Выбор: ");
                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        InsertCoinFlow(vm);
                        break;
                    case "2":
                        BuyFlow(vm);
                        break;
                    case "3":
                        RefundFlow(vm);
                        break;
                    case "9":
                        AdminFlow(vm);
                        break;
                    case "0":
                        return;
                    default:
                        Pause("Неверный ввод");
                        break;
                }
            }
        }

        private static void SeedFloat(CashRegister cash)
        {
            cash.AddFloat(Denomination.R10, 5);
            cash.AddFloat(Denomination.R5, 5);
            cash.AddFloat(Denomination.R2, 10);
            cash.AddFloat(Denomination.R1, 20);
        }

        private static void PrintProducts(Inventory<IProduct> inv)
        {
            Console.WriteLine("Доступные товары:");
            Console.WriteLine("ID   | Название        | Цена  | Кол-во");
            Console.WriteLine("-----+-----------------+-------+-------");
            foreach (var line in inv.Items)
            {
                Console.WriteLine($"{line.Product.Id,-4} | {line.Product.Name,-15} | {line.Product.Price,5} | {line.Quantity,5}");
            }
        }

        private static void InsertCoinFlow(VendingMachine vm)
        {
            Console.Clear();
            Console.WriteLine("Вставить монету:");
            Console.WriteLine("1) 1 ₽   2) 2 ₽   5) 5 ₽   10) 10 ₽");
            Console.Write("Введите номинал (1/2/5/10): ");
            var s = Console.ReadLine();
            if (!TryParseDenom(s, out var denom))
            {
                Pause("Неверный номинал.");
                return;
            }

            vm.InsertCoin(denom);
            Pause($"Ок. Внесено: {vm.CurrentInserted}");
        }

        private static void BuyFlow(VendingMachine vm)
        {
            Console.Clear();
            Console.Write("Введите ID товара: ");
            var id = Console.ReadLine() ?? "";
            var result = vm.Buy(id);
            if (!result.IsSuccess)
            {
                Pause("Покупка не удалась: " + result.Error);
                return;
            }

            var r = result.Value!;
            Console.WriteLine($"Выдали: {r.Product.Name} за {r.Price}, внесено {r.Paid}.");
            Console.WriteLine($"Сдача: {r.ChangeAmount}");
            if (r.Change.Count > 0)
            {
                Console.WriteLine("Монеты сдачи:");
                foreach (var kvp in r.Change.OrderByDescending(k => k.Key))
                    Console.WriteLine($"  {kvp.Key/100} ₽ × {kvp.Value}");
            }
            Pause("Спасибо за покупку!");
        }

        private static void RefundFlow(VendingMachine vm)
        {
            Console.Clear();
            var refund = vm.CancelAndRefund();
            if (refund.Count == 0)
            {
                Pause("Возвращать нечего.");
                return;
            }

            Console.WriteLine("Возвращены монеты:");
            foreach (var kvp in refund.OrderByDescending(k => k.Key))
                Console.WriteLine($"  {kvp.Key/100} ₽ × {kvp.Value}");
            Pause($"Итого возвращено: {CurrencyAmount.FromKopeks(refund.Sum(k => k.Key * k.Value))}");
        }

        private static void AdminFlow(VendingMachine vm)
        {
            Console.Clear();
            Console.Write("PIN: ");
            var pin = Console.ReadLine() ?? "";
            var enter = vm.TryEnterAdmin(pin);
            if (!enter.IsSuccess)
            {
                Pause("Доступ запрещён.");
                return;
            }

            var admin = enter.Value!;
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== АДМИН-МЕНЮ ===");
                Console.WriteLine($"Баланс кассы: {admin.VaultBalance}");
                Console.WriteLine();
                Console.WriteLine("1) Пополнить товар");
                Console.WriteLine("2) Добавить монеты в кассу");
                Console.WriteLine("3) Состояние кассы");
                Console.WriteLine("4) Снять всю наличку");
                Console.WriteLine("0) Назад");
                Console.Write("Выбор: ");
                var ch = Console.ReadLine();

                switch (ch)
                {
                    case "1":
                        Console.Write("ID товара: ");
                        var id = Console.ReadLine() ?? "";
                        Console.Write("На сколько пополнить (шт): ");
                        if (!int.TryParse(Console.ReadLine(), out var qty) || qty <= 0)
                        {
                            Pause("Неверное число.");
                            break;
                        }
                        if (admin.AddStock(id, qty)) Pause("Готово.");
                        else Pause("Товар не найден.");
                        break;

                    case "2":
                        Console.Write("Номинал (1/2/5/10): ");
                        var s = Console.ReadLine();
                        if (!TryParseDenom(s, out var denom))
                        {
                            Pause("Неверный номинал.");
                            break;
                        }
                        Console.Write("Количество монет: ");
                        if (!int.TryParse(Console.ReadLine(), out var cnt) || cnt <= 0)
                        {
                            Pause("Неверное число.");
                            break;
                        }
                        admin.AddFloat(denom, cnt);
                        Pause("Готово.");
                        break;

                    case "3":
                        var (vault, hopper) = admin.CashSnapshot();
                        Console.WriteLine("Vault (в кассе):");
                        PrintCoins(vault);
                        Console.WriteLine("Hopper (внесено клиентами, ещё не зафиксировано):");
                        PrintCoins(hopper);
                        Pause();
                        break;

                    case "4":
                        var collected = admin.CollectAllCash();
                        Console.WriteLine("Снято:");
                        PrintCoins(collected);
                        var total = CurrencyAmount.FromKopeks(collected.Sum(k => k.Key * k.Value));
                        Pause($"Итого: {total}");
                        break;

                    case "0":
                        return;

                    default:
                        Pause("Неверный ввод.");
                        break;
                }
            }
        }

        private static void PrintCoins(Dictionary<int, int> map)
        {
            if (map.Count == 0) { Console.WriteLine("  (пусто)"); return; }
            foreach (var kvp in map.OrderByDescending(k => k.Key))
                Console.WriteLine($"  {kvp.Key/100} ₽ × {kvp.Value}");
        }

        private static bool TryParseDenom(string? s, out Denomination denom)
        {
            denom = Denomination.R1;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            return s switch
            {
                "1" or "1р" or "1₽" => (denom = Denomination.R1) != 0,
                "2" or "2р" or "2₽" => (denom = Denomination.R2) != 0,
                "5" or "5р" or "5₽" => (denom = Denomination.R5) != 0,
                "10" or "10р" or "10₽" => (denom = Denomination.R10) != 0,
                _ => false
            };
        }

        private static void Pause(string? message = null)
        {
            if (!string.IsNullOrEmpty(message)) Console.WriteLine(message);
            Console.Write("Нажмите Enter...");
            Console.ReadLine();
        }
    }

    public static class Program
    {
        public static void Main(string[] args)
        {
            App.Run();
        }
    }
}
