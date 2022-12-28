using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Synto;

public static class SyntaxListExtensions
{
    public static SyntaxList<TSyntax> Capture<TSyntax>(this SyntaxList<TSyntax> list, Action<Action<TSyntax>> block) where TSyntax : SyntaxNode
    {
        List<TSyntax> buffer = new List<TSyntax>();
        block(buffer.Add);
        return list.AddRange(buffer);
    }
}