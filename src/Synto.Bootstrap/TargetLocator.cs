﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace Synto.Bootstrap;

internal sealed class TargetLocator : ISyntaxContextReceiver
{
    public ClassDeclarationSyntax? TargetNode { get; private set; }

    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        if (context.Node is ClassDeclarationSyntax cdl && StringComparer.Ordinal.Equals("CSharpSyntaxQuoter", cdl.Identifier.Text))
        {
            //Debugger.Launch();
            this.TargetNode = cdl;
        }
    }
}