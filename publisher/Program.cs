using LethalModpackUpdater;

try
{
    Packager.Build(args);
    Console.WriteLine("Pacote criado.");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}
