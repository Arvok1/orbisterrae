using System;
using System.Linq;
using Google.Protobuf;
using NUnit.Framework;
using Orbis.Core;
using Orbis.V1;

namespace Orbis.Tests.EditMode
{
    public sealed class EconomyStoreTests
    {
        static HubsState Roster(params string[] names) => new HubsState
        {
            Hubs = { names.Select(name => new HubState { Id = "hub-" + name, Name = name, Fee = 0.02, Stock = { ["grain"] = 7 } }) },
        };
        static MarketSnapshot Market(string hubId, string commodity, double price) => new MarketSnapshot
        {
            HubId = hubId,
            Commodity = commodity,
            LocalPrice = price,
        };
        static CaravanState Caravan(string id, uint revision) => new CaravanState
        {
            Id = id,
            Cargo = { ["grain"] = 5 },
            Metal = 10,
            Revision = revision,
            Status = "traveling",
            DueAtMs = 90_000,
        };

        [Test]
        public void RosterChangesNotifyOnceAndReplaceThePreviousList()
        {
            var store = new EconomyStore();
            var notifications = 0;
            store.Changed += () => notifications++;
            Assert.That(store.Hubs, Is.Empty);

            store.ApplyHubs(Roster("Aldeia", "Porto"));
            store.ApplyHubs(Roster("Porto"));

            Assert.That(store.Hubs.Count, Is.EqualTo(1));
            Assert.That(store.Hubs[0].Name, Is.EqualTo("Porto"));
            Assert.That(notifications, Is.EqualTo(2));
        }

        [Test]
        public void SelectingAHubDropsStaleDetailUntilItsOwnReplyArrives()
        {
            var store = new EconomyStore();
            var notifications = 0;
            store.Changed += () => notifications++;
            store.ApplyHubs(Roster("Aldeia", "Porto"));

            store.SelectHub("hub-Porto");
            Assert.That(store.SelectedHubId, Is.EqualTo("hub-Porto"));
            Assert.That(store.Hub, Is.Null);
            Assert.That(store.Market, Is.Null);

            store.ApplyHub(new HubState { Id = "hub-Aldeia", Name = "Aldeia" });
            Assert.That(store.Hub, Is.Null, "a reply for a hub the player left must be ignored");
            store.ApplyHub(new HubState { Id = "hub-Porto", Name = "Porto", Metal = 50 });
            Assert.That(store.Hub.Metal, Is.EqualTo(50));

            store.ApplyMarket(Market("hub-Porto", "grain", 2.5));
            Assert.That(store.Market.LocalPrice, Is.EqualTo(2.5).Within(.0001));
            store.ApplyMarket(Market("hub-Aldeia", "grain", 9.9));
            Assert.That(store.Market.HubId, Is.EqualTo("hub-Porto"), "foreign market snapshots must not replace the open one");
            Assert.That(store.FindHub("hub-Porto"), Is.Not.Null);
            Assert.That(store.FindHubByName("Porto").Id, Is.EqualTo("hub-Porto"));
            Assert.That(notifications >= 4, Is.True);
        }

        [Test]
        public void SelectingTheSameHubAgainIsANoOpWithoutNotifications()
        {
            var store = new EconomyStore();
            var notifications = 0;
            store.Changed += () => notifications++;
            store.SelectHub("hub-A");
            Assert.That(notifications, Is.EqualTo(1));
            store.SelectHub("hub-A");
            Assert.That(notifications, Is.EqualTo(1), "re-selecting the live hub must not trigger a refresh");
            Assert.That(store.Hub, Is.Null);
        }

        [Test]
        public void CaravansUpsertByIdKeepingTheLatestRevision()
        {
            var store = new EconomyStore();
            store.ApplyCaravan(Caravan("c1", 1));
            store.ApplyCaravan(Caravan("c1", 3));
            store.ApplyCaravan(Caravan("c1", 2));
            store.ApplyCaravan(Caravan("c2", 1));

            Assert.That(store.Caravans.Count, Is.EqualTo(2));
            Assert.That(store.Caravans.Single(caravan => caravan.Id == "c1").Revision, Is.EqualTo(3),
                "an older snapshot must never overwrite a newer one");
        }

        [Test]
        public void ClearEmptiesEverySnapshot()
        {
            var store = new EconomyStore();
            store.ApplyHubs(Roster("Aldeia"));
            store.SelectHub("hub-Aldeia");
            store.ApplyHub(new HubState { Id = "hub-Aldeia", Name = "Aldeia" });
            store.ApplyCaravan(Caravan("c1", 1));

            store.Clear();

            Assert.That(store.Hubs, Is.Empty);
            Assert.That(store.Caravans, Is.Empty);
            Assert.That(store.SelectedHubId, Is.EqualTo(string.Empty));
            Assert.That(store.Hub, Is.Null);
            Assert.That(store.Market, Is.Null);
        }
    }

    public sealed class EconomyCommandTests
    {
        [Test]
        public void CreateCaravanCarriesCityHubRevisionAndCargo()
        {
            var caravan = new CreateCaravan
            {
                CityId = "city-1",
                HubId = "hub-1",
                ExpectedRevision = 17,
                Metal = 5,
            };
            caravan.Cargo.Add("grain", 5);
            var command = new Command { RequestId = "req-1", CreateCaravan = caravan };

            var decoded = Command.Parser.ParseFrom(command.ToByteArray());

            Assert.That(decoded.RequestId, Is.EqualTo("req-1"));
            Assert.That(decoded.CreateCaravan.CityId, Is.EqualTo("city-1"));
            Assert.That(decoded.CreateCaravan.HubId, Is.EqualTo("hub-1"));
            Assert.That(decoded.CreateCaravan.ExpectedRevision, Is.EqualTo(17));
            Assert.That(decoded.CreateCaravan.Metal, Is.EqualTo(5));
            Assert.That(decoded.CreateCaravan.Cargo["grain"], Is.EqualTo(5));
        }

        [Test]
        public void ListHubsRoundTripsAsAnEmptyCommand()
        {
            var decoded = Command.Parser.ParseFrom(new Command
            {
                RequestId = "roster",
                ListHubs = new ListHubs(),
            }.ToByteArray());

            Assert.That(decoded.ListHubs, Is.Not.Null);
            Assert.That(decoded.RequestId, Is.EqualTo("roster"));
        }

        [Test]
        public void HubRosterCaravanAndMarketMessagesRoundTrip()
        {
            var message = new ServerMsg
            {
                Hubs = new HubsState
                {
                    Hubs = { new HubState { Id = "hub-1", Name = "Feira do Vale", Fee = .02, Stock = { ["grain"] = 900 }, Metal = 50_000 } },
                },
            };
            var decoded = ServerMsg.Parser.ParseFrom(message.ToByteArray());
            Assert.That(decoded.Hubs.Hubs[0].Name, Is.EqualTo("Feira do Vale"));
            Assert.That(decoded.Hubs.Hubs[0].Stock["grain"], Is.EqualTo(900));

            message = new ServerMsg { Caravan = new CaravanState { Id = "c1", Status = "at_hub", Cargo = { ["grain"] = 5 }, DueAtMs = 90_000 } };
            decoded = ServerMsg.Parser.ParseFrom(message.ToByteArray());
            Assert.That(decoded.Caravan.Status, Is.EqualTo("at_hub"));
            Assert.That(decoded.Caravan.DueAtMs, Is.EqualTo(90_000));

            message = new ServerMsg { Market = new MarketSnapshot { HubId = "hub-1", Commodity = "grain", LocalPrice = 2.4 } };
            decoded = ServerMsg.Parser.ParseFrom(message.ToByteArray());
            Assert.That(decoded.Market.LocalPrice, Is.EqualTo(2.4).Within(.0001));
        }

        [Test]
        public void ReceiptsAcceptTheReadRepliesWithoutACity()
        {
            var receipt = new Orbis.Network.CommandReceipt(7, 3, "roster", "", 0, 0);
            var hubs = new Envelope { Seq = 7, Server = new ServerMsg { Hubs = new HubsState() } };
            Assert.That(receipt.Accept(hubs, 3), Is.True, "hub replies carry no city and should not be dropped");

            var caravan = new Envelope { Seq = 7, Server = new ServerMsg { Caravan = new CaravanState { Id = "c1" } } };
            Assert.That(receipt.Accept(caravan, 3), Is.True);
        }
    }
}
