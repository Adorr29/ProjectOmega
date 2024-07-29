namespace ProjectOmega
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Omega omega = new Omega();
            omega.Start().GetAwaiter().GetResult();
        }
    }
}