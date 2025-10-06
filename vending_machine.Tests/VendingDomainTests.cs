
using System;
using System.Collections.Generic;
using System.Linq;
using vending_machine;   // ← пространство имён твоего приложения (должно совпадать с namespace в Program.cs)
using Xunit;

namespace vending_machine.Tests
{
    public class MoneyAndCurrencyAmountTests
    {
        [Fact]
        public void ToString_FormatsCurrencyWithCommaAndRubleSign()
        {
            var amount = CurrencyAmount.FromRubles(12.34m);
            Assert.Equal("12,34 ₽", amount.ToString());
        }

        [Theory]
        [InlineData(1.005, 101)]
        [InlineData(1.004, 100)]
        [InlineData(0.995, 100)]
        public void FromRubles_RoundsAwayFromZero(decimal rubles, int expectedKopeks)
        {
            var amount = CurrencyAmount.FromRubles(rubles);
            Assert.Equal(expectedKopeks, amount.Kopeks);
        }

        [Fact]
        public void Operators_Add_Subtract_Compare_WorkCorrectly()
        {
            var a = CurrencyAmount.FromRubles(10m);   // 1000
            var b = CurrencyAmount.FromRubles(3.50m); // 350
            Assert.Equal(1350, (a + b).Kopeks);
            Assert.Equal(650,  (a - b).Kopeks);
            Assert.True(a >= b);
            Assert.True(b <= a);
        }
    }

    public class ChangeStrategyTests
    {
        [Fact]
        public void Greedy_TryMakeChange_Succeeds_WithLimitedCoins()
        {
            var available = new Dictionary<int, int> { [1000]=1, [500]=1, [200]=2, [100]=3 };
            var strategy = new GreedyChangeStrategy();
            var ok = strategy.TryMakeChange(1300, available, out var change);
            Assert.True(ok);
            Assert.Equal(1, change[1000]);
            Assert.Equal(1, change[200]);
            Assert.Equal(1, change[100]);
            Assert.Equal(3, change.Values.Sum());
        }

        [Fact]
        public void Greedy_TryMakeChange_Fails_WhenOddSumButOnlyEvenCoins()
        {
            var strategy = new GreedyChangeStrategy();
            var ok = strategy.TryMakeChange(300, new Dictionary<int,int>{ [200]=10 }, out var change);
            Assert.False(ok);
            Assert.Empty(change);
        }

        [Fact]
        public void Greedy_TryMakeChange_ZeroAmount_IsTriviallyPossible()
        {
            var strategy = new GreedyChangeStrategy();
            var ok = strategy.TryMakeChange(0, new Dictionary<int,int> { [1000]=1 }, out var change);
            Assert.True(ok);
            Assert.Empty(change);
        }
    }

    public class CashRegisterTests
    {
        [Fact]
        public void InsertAndRefund_ReturnsExactlyWhatWasInserted_AndEmptiesHopper()
        {
            var cash = new CashRegister(new GreedyChangeStrategy());
            cash.Insert(new Coin(Denomination.R2));
            cash.Insert(new Coin(Denomination.R1));
            Assert.Equal(300, cash.InsertedAmountKopeks);
            var refund = cash.RefundInserted();
            Assert.Equal(1, refund[200]);
            Assert.Equal(1, refund[100]);
            Assert.Equal(0, cash.InsertedAmountKopeks);
        }

        [Fact]
        public void AddFloat_IncreasesVault_EmptyVault_EmptiesAndReturnsCoins()
        {
            var cash = new CashRegister(new GreedyChangeStrategy());
            cash.AddFloat(Denomination.R10, 2);
            cash.AddFloat(Denomination.R5,  1);
            Assert.Equal(2500, cash.VaultBalanceKopeks);
            var all = cash.EmptyVault();
            Assert.Equal(2, all[1000]);
            Assert.Equal(1, all[500]);
            Assert.Equal(0, cash.VaultBalanceKopeks);
        }

        [Fact]
        public void CanMakeChange_ConsidersVaultAndHopperTogether()
        {
            var cash = new CashRegister(new GreedyChangeStrategy());
            cash.AddFloat(Denomination.R2, 10);     // vault: только 2 ₽
            cash.Insert(new Coin(Denomination.R1)); // hopper: 1 ₽
            Assert.True(cash.CanMakeChange(100));   // 1 руб возможна из (vault+hopper)
        }
    }

    public class VendingMachineTests
    {
        private static Inventory<IProduct> OneItemInventory(string id, string name, decimal priceRub, int qty)
        {
            var inv = new Inventory<IProduct>();
            inv.AddNew(new Drink(id, name, CurrencyAmount.FromRubles(priceRub)), qty);
            return inv;
        }

        private static void SeedFloat(CashRegister cash)
        {
            cash.AddFloat(Denomination.R10, 10);
            cash.AddFloat(Denomination.R5,  10);
            cash.AddFloat(Denomination.R2,  20);
            cash.AddFloat(Denomination.R1,  50);
        }

        [Fact]
        public void Buy_Succeeds_AndGivesCorrectChange()
        {
            var inv  = OneItemInventory("I1", "Тестовый напиток", 7m, qty: 5);
            var cash = new CashRegister(new GreedyChangeStrategy());
            SeedFloat(cash);
            var vm   = new VendingMachine(inv, cash, "1234");

            vm.InsertCoin(Denomination.R10);
            var result = vm.Buy("I1");

            Assert.True(result.IsSuccess);
            Assert.Equal("Тестовый напиток", result.Value!.Product.Name);
            Assert.Equal(1000, result.Value.Paid.Kopeks);
            Assert.Equal(700,  result.Value.Price.Kopeks);
            Assert.Equal(300,  result.Value.ChangeAmount.Kopeks);
            Assert.Equal(1, result.Value.Change[200]);
            Assert.Equal(1, result.Value.Change[100]);
        }

        [Fact]
        public void Buy_Fails_WhenInsufficientFunds()
        {
            var inv  = OneItemInventory("I1", "Снек", 7m, qty: 1);
            var cash = new CashRegister(new GreedyChangeStrategy());
            SeedFloat(cash);
            var vm   = new VendingMachine(inv, cash, "1234");

            vm.InsertCoin(Denomination.R5);
            var result = vm.Buy("I1");
            Assert.False(result.IsSuccess);
        }

        [Fact]
        public void Buy_Fails_WhenChangeImpossible()
        {
            var inv  = OneItemInventory("I1", "Напиток", 7m, qty: 1);
            var cash = new CashRegister(new GreedyChangeStrategy());
            cash.AddFloat(Denomination.R2, 10); // без 1 ₽
            var vm   = new VendingMachine(inv, cash, "1234");

            vm.InsertCoin(Denomination.R10);
            Assert.False(vm.Buy("I1").IsSuccess);
        }

        [Fact]
        public void CancelAndRefund_ReturnsInsertedCoins_AndResetsInsertedAmount()
        {
            var inv  = OneItemInventory("I1", "Снек", 5m, qty: 1);
            var cash = new CashRegister(new GreedyChangeStrategy());
            var vm   = new VendingMachine(inv, cash, "1234");

            vm.InsertCoin(Denomination.R2);
            vm.InsertCoin(Denomination.R1);
            var refund = vm.CancelAndRefund();

            Assert.Equal(1, refund[200]);
            Assert.Equal(1, refund[100]);
            Assert.Equal(0, vm.CurrentInserted.Kopeks);
        }

        [Fact]
        public void Buy_DecreasesInventory()
        {
            var inv  = OneItemInventory("I1", "Вода", 2m, qty: 2);
            var cash = new CashRegister(new GreedyChangeStrategy());
            SeedFloat(cash);
            var vm   = new VendingMachine(inv, cash, "1234");

            vm.InsertCoin(Denomination.R2);
            var result = vm.Buy("I1");
            Assert.True(result.IsSuccess);

            var left = inv.Items.Single(x => x.Product.Id == "I1").Quantity;
            Assert.Equal(1, left);
        }

        [Fact]
        public void Buy_Fails_WhenProductOutOfStock()
        {
            var inv  = OneItemInventory("I1", "Редкий товар", 2m, qty: 0);
            var cash = new CashRegister(new GreedyChangeStrategy());
            SeedFloat(cash);
            var vm   = new VendingMachine(inv, cash, "1234");

            vm.InsertCoin(Denomination.R2);
            Assert.False(vm.Buy("I1").IsSuccess);
        }

        [Fact]
        public void Buy_Fails_WhenProductNotFound()
        {
            var inv  = OneItemInventory("I1", "Товар", 2m, qty: 1);
            var cash = new CashRegister(new GreedyChangeStrategy());
            SeedFloat(cash);
            var vm   = new VendingMachine(inv, cash, "1234");

            vm.InsertCoin(Denomination.R2);
            Assert.False(vm.Buy("UNKNOWN").IsSuccess);
        }
    }

    public class AdminSessionTests
    {
        private static (VendingMachine vm, AdminSession admin) CreateWithAdminSession()
        {
            var inv = new Inventory<IProduct>();
            inv.AddNew(new Snack("S1", "Шоколад", CurrencyAmount.FromRubles(10m)), 1);
            var cash = new CashRegister(new GreedyChangeStrategy());
            var vm = new VendingMachine(inv, cash, "4321");
            var enter = vm.TryEnterAdmin("4321");
            Assert.True(enter.IsSuccess);
            return (vm, enter.Value!);
        }

        [Fact]
        public void TryEnterAdmin_WrongPin_Fails()
        {
            var inv = new Inventory<IProduct>();
            var cash = new CashRegister(new GreedyChangeStrategy());
            var vm = new VendingMachine(inv, cash, "9999");
            Assert.False(vm.TryEnterAdmin("0000").IsSuccess);
        }

        [Fact]
        public void Admin_AddStock_IncreasesQuantity()
        {
            var (vm, admin) = CreateWithAdminSession();
            var before = admin.ProductsSnapshot().Single(x => x.Product.Id == "S1").Quantity;
            Assert.True(admin.AddStock("S1", 5));
            var after = admin.ProductsSnapshot().Single(x => x.Product.Id == "S1").Quantity;
            Assert.Equal(before + 5, after);
        }

        [Fact]
        public void Admin_AddFloat_IncreasesVault_And_CollectAllCash_EmptiesVault()
        {
            var (vm, admin) = CreateWithAdminSession();
            admin.AddFloat(Denomination.R10, 3);
            admin.AddFloat(Denomination.R5,  2);
            Assert.Equal(CurrencyAmount.FromRubles(40m).Kopeks, admin.VaultBalance.Kopeks);
            var collected = admin.CollectAllCash();
            Assert.Equal(3, collected[1000]);
            Assert.Equal(2, collected[500]);
            Assert.Equal(0, admin.VaultBalance.Kopeks);
        }

        [Fact]
        public void Admin_Snapshot_ShowsVaultAndHopper()
        {
            var (vm, admin) = CreateWithAdminSession();
            admin.AddFloat(Denomination.R1, 2);
            vm.InsertCoin(Denomination.R2);
            var (vault, hopper) = admin.CashSnapshot();
            Assert.Equal(2, vault[100]);
            Assert.Equal(1, hopper[200]);
        }
    }
}
