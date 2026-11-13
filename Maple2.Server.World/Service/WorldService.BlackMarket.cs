using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;


namespace Maple2.Server.World.Service;

public partial class WorldService {
    public override Task<BlackMarketResponse> BlackMarket(BlackMarketRequest request, ServerCallContext context) {
        switch (request.BlackMarketCase) {
            case BlackMarketRequest.BlackMarketOneofCase.Search:
                return Task.FromResult(Search(request.Search));
            case BlackMarketRequest.BlackMarketOneofCase.Add:
                return Task.FromResult(Add(request.Add));
            case BlackMarketRequest.BlackMarketOneofCase.Remove:
                return Task.FromResult(Remove(request.Remove));
            case BlackMarketRequest.BlackMarketOneofCase.Purchase:
                return Task.FromResult(Purchase(request.Purchase));
            default:
                return Task.FromResult(new BlackMarketResponse());
        }
    }

    private BlackMarketResponse Search(BlackMarketRequest.Types.Search search) {
        Dictionary<BasicAttribute, BasicOption> basicOptions = new();
        Dictionary<SpecialAttribute, SpecialOption> specialOptions = new();
        if (search.StatOptions != null) {
            foreach (StatOption statOption in search.StatOptions) {
                ReadStat(basicOptions, specialOptions, statOption.StatId, statOption.Value);
            }
        }

        ICollection<long> listingIds = blackMarketLookup.Search(search.Categories.ToArray(), search.MinLevel, search.MaxLevel, (JobFilterFlag) search.JobFlag, search.Rarity, search.MinEnchantLevel,
            search.MaxEnchantLevel, search.MinSocketCount, search.MaxSocketCount, search.Name, search.StartPage, (BlackMarketSort) search.SortBy, basicOptions, specialOptions);


        return new BlackMarketResponse {
            Search = new BlackMarketResponse.Types.Search {
                ListingIds = {
                    listingIds,
                },
            },
        };
    }

    // One search option: statId is the client's key (BlackMarketStatKey) with the server's basic stat id
    // (BlackMarketHandler.ServerStatKey). A rate comes as the client sends it, in hundredths of a percent less 5
    // (UIBlackMarketPage, uiblackmarketpage.cpp:910-929). A stat the server has no member for is left out.
    public static void ReadStat(Dictionary<BasicAttribute, BasicOption> basicOptions, Dictionary<SpecialAttribute, SpecialOption> specialOptions, int statId, int value) {
        BlackMarketStatKey? key = BlackMarketStatKey.Decode(statId);
        if (key is not { } stat || stat.Stat > byte.MaxValue) {
            return;
        }
        float rate = (float) (value + 5) / 10000;
        if (stat.Special) {
            if (!Enum.IsDefined((SpecialAttribute) stat.Stat)) {
                return;
            }
            specialOptions[(SpecialAttribute) stat.Stat] = stat.Percent ? new SpecialOption(rate) : new SpecialOption(value);
            return;
        }
        if (!Enum.IsDefined((BasicAttribute) stat.Stat)) {
            return;
        }
        basicOptions[(BasicAttribute) stat.Stat] = stat.Percent ? new BasicOption(rate) : new BasicOption(value);
    }

    private BlackMarketResponse Add(BlackMarketRequest.Types.Add add) {
        BlackMarketError error = blackMarketLookup.Add(add.ListingId);
        return new BlackMarketResponse {
            Error = (int) error,
        };
    }

    private BlackMarketResponse Remove(BlackMarketRequest.Types.Remove remove) {
        BlackMarketError error = blackMarketLookup.Remove(remove.ListingId);
        return new BlackMarketResponse {
            Error = (int) error,
        };
    }

    private BlackMarketResponse Purchase(BlackMarketRequest.Types.Purchase purchase) {
        BlackMarketError error = blackMarketLookup.Purchase(purchase.ListingId);
        if (error != BlackMarketError.none) {
            return new BlackMarketResponse {
                Error = (int) error,
            };
        }

        PlayerInfo? playerInfo = playerLookup.GetPlayerInfo(purchase.SellerId);
        if (playerInfo != null && channelClients.TryGetClient(playerInfo.Channel, out ChannelClient? client)) {
            client.BlackMarket(new Channel.Service.BlackMarketRequest {
                PurchaseResponse = new Channel.Service.BlackMarketRequest.Types.PurchaseResponse {
                    SellerId = playerInfo.CharacterId,
                },
            });
        }

        return new BlackMarketResponse();
    }
}
