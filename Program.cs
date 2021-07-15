using System;
using System.Timers;
using System.Device.I2c;
using System.Device.Gpio;
using System.Threading;
using System.IO.Ports;

namespace i2ctest
{
    class Program
    {

        private static System.Timers.Timer timer;

        private static I2cDevice dev;

        private static int counter = 0;
            
        // declare arrays to convert readings
        private static byte[] co2Bytes = new byte[4];
        private static byte[] tempBytes = new byte[4];
        private static byte[] rhBytes = new byte[4];

        // define SCD30 commands
        private static byte[] startCmd = new byte[] {0x00, 0x10, 0x00, 0x00}; 
        private static byte[] statusCmd = new byte[] {0x02, 0x02};
        private static byte[] readCmd = new byte[] {0x03, 0x00};

        private static SerialPort port = null;

        static void Main(string[] args)
        {
            Console.WriteLine("Lora Publisher for SCD30 Sensor");
            Console.WriteLine("Press any key to quit...");

            const int busId = 1;
            const int devAddr = 0x61;

            // set conn. params
            var con = new I2cConnectionSettings(busId, devAddr);

            // create I2C device
            dev = I2cDevice.Create(con);

            // send start continuous reading command
            dev.Write(startCmd.AsSpan());

            // configure E32 Lora module
            configureE32();

            // start event timer
            setTimer();

            // wait for keypress
            while(true)
            {
                if (Console.KeyAvailable) break;
            }
        }

        // setup and start timer
        private static void setTimer()
        {
            // Create a timer with a ten second interval.
            timer = new System.Timers.Timer(10000);
            // Hook up the Elapsed event for the timer. 
            timer.Elapsed += OnTimedEvent;
            timer.AutoReset = true;
            timer.Enabled = true;
        }

        // Configure E32 module
        private static void configureE32()
        {

            try
            {
                const int GPIO0 = 17;
                const int GPIO1 = 18;
                var controller = new GpioController();

                // put into config mode
                controller.OpenPin(GPIO0, PinMode.Output);
                controller.OpenPin(GPIO1, PinMode.Output);
                controller.Write(GPIO0, PinValue.High);
                controller.Write(GPIO1, PinValue.High);

                var configure = new byte[] { 0xC2, 0x00, 0x01, 0x1A, 0x17, 0x44 };

                var portName = "/dev/serial0";

                // create serial port
                port = new SerialPort()
                {
                    PortName = portName,
                    BaudRate = 9600,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One
                };

                // open port
                port.Open();

                // write config
                port.Write(configure, 0, configure.Length);

                // short delay
                System.Threading.Thread.Sleep(10);

                // put into normal mode
                controller.Write(GPIO0, PinValue.Low);
                controller.Write(GPIO1, PinValue.Low);
            }
            catch (System.Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        // timer elapsed handler
        private static void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            Console.WriteLine(e.SignalTime.ToString());

            // declare buffers
            var arr = new byte[18];
            var statusBuffer = new Span<byte>(arr, 0, 2);
            var dataBuffer = new Span<byte>(arr, 0, 18);

            // check if reading ready
            do
            {
                dev.Write(statusCmd.AsSpan());
                Thread.Sleep(100);
                dev.Read(statusBuffer);

                if (statusBuffer[1] != 1) Thread.Sleep(100);

            } while (statusBuffer[1] != 1);

            // request readings
            dev.Write(readCmd.AsSpan());

            // short wait
            Thread.Sleep(100);

            // read data
            dev.Read(dataBuffer);

            // convert to floats
            co2Bytes[0] = dataBuffer[4];
            co2Bytes[1] = dataBuffer[3];
            co2Bytes[2] = dataBuffer[1];
            co2Bytes[3] = dataBuffer[0];

            tempBytes[0] = dataBuffer[10];
            tempBytes[1] = dataBuffer[9];
            tempBytes[2] = dataBuffer[7];
            tempBytes[3] = dataBuffer[6];

            rhBytes[0] = dataBuffer[16];
            rhBytes[1] = dataBuffer[15];
            rhBytes[2] = dataBuffer[13];
            rhBytes[3] = dataBuffer[12];

            var co2 = BitConverter.ToSingle(co2Bytes,0);
            var temp = BitConverter.ToSingle(tempBytes,0);
            var rh = BitConverter.ToSingle(rhBytes,0);

            // report to console
            Console.WriteLine($"Readings = {++counter}");
            Console.WriteLine($"CO2 = {co2:F2}");
            Console.WriteLine($"Temp = {temp:F2}");
            Console.WriteLine($"R.H. = {rh:F2}");
            Console.WriteLine();

            // write to serial port
            var loramsg = $"{e.SignalTime.Hour:d02}:{e.SignalTime.Minute:d02}:{e.SignalTime.Second:d02} {co2:F2},{temp:F2},{rh:F2}\r";
            port.Write(loramsg);
        }
    }
}
