using Maple2.File.Ingest.Mapper;
using ParserType = Maple2.File.Parser.Enum.ItemExchangeScrollType;
using ItemExchangeScrollType = Maple2.Model.Metadata.ItemExchangeScrollType;

namespace Maple2.Server.Tests.Ingest;

// A row's type as the client reads it (CExchangeItemInfo::m_eType, itemexchangescrolltable.cpp:108): 1 exchange,
// 2 upgrade, 3 trading. Before, the ingest dropped it and every row was an exchange.
public class ItemExchangeScrollIngestTests {
    [TestCase(ParserType.upgrade, ItemExchangeScrollType.Upgrade)]
    [TestCase(ParserType.trading, ItemExchangeScrollType.Trading)]
    [TestCase(ParserType.exchange, ItemExchangeScrollType.Exchange)]
    [TestCase(ParserType.updated, ItemExchangeScrollType.Exchange)]
    [TestCase(ParserType.unknown, ItemExchangeScrollType.Exchange)]
    public void RowType_IsKept(ParserType type, ItemExchangeScrollType expected) {
        Assert.That(TableMapper.ExchangeScrollType(type), Is.EqualTo(expected));
    }
}
