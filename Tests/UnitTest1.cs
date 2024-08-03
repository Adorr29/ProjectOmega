namespace Tests
{
    public class UnitTest1
    {
        [Fact]
        public void TestPass()
        {

        }

        [Fact]
        public void TestFail()
        {
            Assert.Fail();
        }
    }
}