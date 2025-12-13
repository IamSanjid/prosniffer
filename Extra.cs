using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Parsing;
using System.Text;

namespace PROSniffer;

public class StringBuilderTextWriter : TextWriter
{
    private readonly StringBuilder _buffer = new();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        _buffer.Append(value);
    }

    public string GetContent()
    {
        return _buffer.ToString();
    }

    public override void Flush()
    {
        // Optional: do something when flushed
        base.Flush();
    }
}

public class PROSnifferCommand(string name, string? description = null) : Command(name, description)
{
    readonly Dictionary<string, Option> options = [];
    readonly Dictionary<string, Argument> arguments = [];

    public void AddOption(Option option)
    {
        options[option.Name] = option;
        Add(option);
    }

    public void AddArgument(Argument argument)
    {
        arguments[argument.Name] = argument;
        Add(argument);
    }

    public Option? GetOption(string name)
    {
        options.TryGetValue(name, out var option);
        return option;
    }

    public Argument? GetArgument(string name)
    {
        arguments.TryGetValue(name, out var argument);
        return argument;
    }
}

public class CommandBuilder(string name)
{
    readonly PROSnifferCommand _makingCommand = new(name);

    public CommandBuilder Description(string description)
    {
        _makingCommand.Description = description;
        return this;
    }

    public CommandBuilder Add(Argument option)
    {
        _makingCommand.AddArgument(option);
        return this;
    }

    public CommandBuilder Add(Option option)
    {
        _makingCommand.AddOption(option);
        return this;
    }

    public PROSnifferCommand Build()
    {
        return _makingCommand;
    }
}

public class OptionBuilder<T>(string name, params string[] aliases)
{
    readonly Option<T> _makingOption = new Option<T>(name, aliases);

    public OptionBuilder<T> Description(string description)
    {
        _makingOption.Description = description;
        return this;
    }

    public OptionBuilder<T> Required(bool required = true)
    {
        _makingOption.Required = required;
        return this;
    }

    public OptionBuilder<T> CompletionSource(Func<CompletionContext, IEnumerable<CompletionItem>> source)
    {
        _makingOption.CompletionSources.Add(source);
        return this;
    }

    public OptionBuilder<T> DefaultValueFactory(Func<ArgumentResult, T> factory)
    {
        _makingOption.DefaultValueFactory = factory;
        return this;
    }

    public Option Build()
    {
        return _makingOption;
    }
}
