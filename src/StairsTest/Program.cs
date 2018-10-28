using System;
using System.Threading;
using System.Threading.Tasks;
using Unosquare.RaspberryIO;
using Unosquare.RaspberryIO.Gpio;

namespace StairsTest
{
	public class Program
	{
		public static async Task Main()
		{
			var programInstance = new ProgramInstance(4, 5, 1);
			Console.CancelKeyPress += async (sender, args) =>
			{
				Console.WriteLine("Cancel requested");
				await programInstance.Stop();
				Console.WriteLine("Application stopped");
			};

			programInstance.Start();

			programInstance.PirStatusChanged += detected =>
			{
				programInstance.FadeLedsAsync(detected);
			};

			await programInstance.RunningTask;
		}
	}

	public class ProgramInstance
	{
		private const int PwmStepCount = 100;
		private const int PinCheckIntervalMs = 100;

		public delegate void PirStatusChangedDelegate(bool movementDetected);

		public event PirStatusChangedDelegate PirStatusChanged;

		public Task RunningTask { get; private set; }

		private readonly GpioPin _pirGpio;
		private readonly GpioPin _lightSensorGpio;
		private readonly GpioPin _ledPin;

		private bool _stopRequested;
		private bool _lastPirStatus;
		private Task _fadeTask;

		private CancellationTokenSource _ledFadeCancellationTokenSource;
		
		public ProgramInstance(int pirPinNumber, int lightSensorPinNumber, int ledPinNumber)
		{
			_pirGpio = Pi.Gpio[pirPinNumber];
			_pirGpio.PinMode = GpioPinDriveMode.Input;
			_pirGpio.InputPullMode = GpioPinResistorPullMode.PullUp;

			_lightSensorGpio = Pi.Gpio[lightSensorPinNumber];
			_lightSensorGpio.PinMode = GpioPinDriveMode.Input;

			_ledPin = Pi.Gpio[ledPinNumber];
			_ledPin.PinMode = GpioPinDriveMode.PwmOutput;
			_ledPin.PwmMode = PwmMode.Balanced;
			_ledPin.PwmClockDivisor = 16;

			_ledPin.PwmRegister = 0;
		}

		public void Start()
		{
			if (_stopRequested)
				throw new InvalidOperationException("Can't start once stopped. Create new instance.");

			RunningTask = Task.Run((Action)PinCheck);
		}

		private void PinCheck()
		{
			while (!_stopRequested)
			{
				var pirStatus = _pirGpio.Read();
				if (pirStatus != _lastPirStatus)
				{
					_lastPirStatus = pirStatus;
					PirStatusChanged?.Invoke(pirStatus);
				}
				
				Thread.Sleep(PinCheckIntervalMs);
			}
		}

		public Task FadeLedsAsync(bool on) =>
			_fadeTask = Task.Run(() => FadeLeds(on));

		private void FadeLeds(bool on)
		{
			_ledFadeCancellationTokenSource?.Cancel(false);
			_ledFadeCancellationTokenSource = new CancellationTokenSource();
			var cancellationToken = _ledFadeCancellationTokenSource.Token;

			if (on)
			{
				for (var i = 0; i <= 100; i++)
				{
					_ledPin.PwmRegister = (int)(_ledPin.PwmRange / PwmStepCount * i);
					Thread.Sleep((int)_ledPin.PwmRange / PwmStepCount);

					if (cancellationToken.IsCancellationRequested)
						return;
				}
			}
			else
			{
				for (var i = 100; i >= 0; i--)
				{
					_ledPin.PwmRegister = (int)(_ledPin.PwmRange / PwmStepCount * i);
					Thread.Sleep((int)_ledPin.PwmRange / PwmStepCount);

					if (cancellationToken.IsCancellationRequested)
						return;
				}
			}
		}

		private async Task ToggleLeds(bool on)
		{
			_ledFadeCancellationTokenSource?.Cancel(false);
			if (_fadeTask?.IsCompleted == false)
				await _fadeTask;

			_ledPin.PinMode = GpioPinDriveMode.Output;
			_ledFadeCancellationTokenSource?.Cancel(false);
			_ledPin.Write(on);
		}

		public async Task Stop()
		{
			_stopRequested = true;
			await ToggleLeds(false);
		}
	}
}
