using NoticeForge.Domain;

namespace NoticeForge.Tests.Domain;

public class CopyleftTextTests
{
    [Theory]
    [InlineData("                    GNU GENERAL PUBLIC LICENSE\n                       Version 3", "GNU GENERAL PUBLIC LICENSE")]
    [InlineData("\t\t  GNU LESSER GENERAL PUBLIC LICENSE", "GNU LESSER GENERAL PUBLIC LICENSE")]
    [InlineData("the GNU Library General\n Public License", "GNU Library General Public License")]   // spans lines
    [InlineData("GNU Affero General Public License v3", "GNU Affero General Public License")]
    public void Finds_gnu_license_names(string text, string expected) =>
        Assert.Equal([expected], CopyleftText.Find(text));

    [Theory]
    [InlineData("The MIT license is compatible with both the GPL and commercial software")]   // a mention found in CodePromptus's notices
    [InlineData("Licensed under the Apache License, Version 2.0")]
    [InlineData("GNU Free Documentation License")]
    public void Ignores_mere_mentions(string text) =>
        Assert.Empty(CopyleftText.Find(text));

    [Fact]
    public void Mpl_secondary_license_definition_is_not_a_finding()
    {
        // An excerpt of the full MPL-2.0 text included in grpc's LICENSE
        const string mpl = """
            1.12. "Secondary License"
                means either the GNU General Public License, Version 2.0, the GNU
                Lesser General Public License, Version 2.1, the GNU Affero General
                Public License, Version 3.0, or any later versions of those
                licenses.
            """;
        Assert.Empty(CopyleftText.Find(mpl));
        // A real one elsewhere in the same document is still found
        Assert.Equal(["GNU GENERAL PUBLIC LICENSE"], CopyleftText.Find(mpl + "\n\n                    GNU GENERAL PUBLIC LICENSE\n"));
    }

    [Theory]
    // .NET's THIRD-PARTY-NOTICES (the note on Mono's memcheck.h)
    [InlineData("""
           Notice that the following BSD-style license applies to this one
           file (memcheck.h) only.  The rest of Valgrind is licensed under the
           terms of the GNU General Public License, version 2, unless
           otherwise indicated.
        """)]
    [InlineData("""
           (memcheck.h) only.  The entire rest of Valgrind is licensed under
           the terms of the GNU General Public License, version 2.  See the
        """)]
    // A boilerplate clause in Microsoft's license terms
    [InlineData("Notwithstanding any other terms, you may reverse engineer this software to the extent\nrequired to debug changes to any libraries licensed under the GNU Lesser General Public License.")]
    public void Known_benign_sentences_are_not_findings(string text) =>
        Assert.Empty(CopyleftText.Find(text));

    [Theory]
    // "You may choose the GPL" needs human review (Skia's notices)
    [InlineData(" * Alternatively, the contents of this file may be used under the terms of\n * either the GNU General Public License Version 2 or later (the \"GPL\"), or")]
    [InlineData("  - The GNU General Public License version 2, found in  `GPLv2.TXT' (any")]
    // Sentences that resemble the boilerplate but differ are not excluded
    [InlineData("The rest of this library is licensed under the terms of the GNU General Public License")]
    public void Choices_and_lookalikes_are_still_found(string text) =>
        Assert.NotEmpty(CopyleftText.Find(text));

    [Theory]
    [InlineData("GPL-2.0-only", true)]
    [InlineData("MIT OR LGPL-2.1-or-later", true)]
    [InlineData("AGPL-3.0-only", true)]
    [InlineData("MIT", false)]
    [InlineData("MPL-2.0", false)]
    [InlineData(null, false)]
    public void Declared_gpl_family(string? expression, bool expected) =>
        Assert.Equal(expected, CopyleftText.IsDeclared(expression));
}
