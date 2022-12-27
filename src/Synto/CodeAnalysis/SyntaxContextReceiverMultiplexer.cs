using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Synto.CodeAnalysis;

public class SyntaxContextReceiverMultiplexer : ISyntaxContextReceiver
{
    private readonly ISyntaxContextReceiver[] _contextReceivers;

    public SyntaxContextReceiverMultiplexer(params ISyntaxContextReceiver[] contextReceivers)
    {
        _contextReceivers = contextReceivers;
    }
    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        Array.ForEach(_contextReceivers, scr => scr.OnVisitSyntaxNode(context));
    }

    public T? OfType<T>() where T : ISyntaxContextReceiver => _contextReceivers.OfType<T>().SingleOrDefault();
}