using System;
using System.Collections.Generic;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Core;

public class ConstantsListTests {
    // Client: Constants::Values<T>::Define reads key0, key1, ... up to the first missing row.
    [Test]
    public void ListConstant_ReadsUntilARowIsMissing() {
        var rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["WarpOpenContinent0"] = "102", ["WarpOpenContinent1"] = "103", ["WarpOpenContinent2"] = "202",
            ["WarpOpenContinent3"] = "105", ["WarpOpenContinent4"] = " 110 ", ["WarpOpenContinent6"] = "999",
        };

        Assert.That(ConstantsTable.ReadList(rows, "WarpOpenContinent"), Is.EqualTo(new[] { 102, 103, 202, 105, 110 }));
        Assert.That(ConstantsTable.ReadList(rows, "Other"), Is.Empty);
    }

    [Test]
    public void OpenContinents_UsesTheListWhenTheTableHasIt() {
        var old = new ConstantsTable(WarpOpenContinent0: 102, WarpOpenContinent1: 103);
        Assert.That(old.OpenContinents(), Is.EqualTo(new[] { 102, 103 }));

        ConstantsTable full = old with { WarpOpenContinents = [102, 103, 202, 105, 110] };
        Assert.That(full.OpenContinents(), Is.EqualTo(new[] { 102, 103, 202, 105, 110 }));
    }
}
