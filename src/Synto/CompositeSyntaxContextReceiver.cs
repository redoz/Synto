using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Synto;

public class CompositeSyntaxContextReceiver : ISyntaxContextReceiver
{
    private readonly ISyntaxContextReceiver[] _contextReceivers;

    public CompositeSyntaxContextReceiver(params ISyntaxContextReceiver[] contextReceivers)
    {
        _contextReceivers = contextReceivers;
    }
    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        Array.ForEach(_contextReceivers, scr => scr.OnVisitSyntaxNode(context));
    }

    public T? OfType<T>() where T : ISyntaxContextReceiver => _contextReceivers.OfType<T>().SingleOrDefault();
}