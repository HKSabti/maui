using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Maui.Controls.Core.UnitTests
{
	public class ShellParameterPassingTests : ShellTestBase
	{
		[TestCase(true)]
		[TestCase(false)]
		public async Task ReNavigatingToCurrentLocationPassesParameters(bool useDataTemplates)
		{
			var shell = new Shell();
			ShellTestPage pagetoTest = new ShellTestPage();
			pagetoTest.BindingContext = pagetoTest;
			var one = CreateShellItem(pagetoTest, shellContentRoute: "content", templated: useDataTemplates);
			shell.Items.Add(one);
			ShellTestPage page = null;
			if (useDataTemplates)
			{
				page = (ShellTestPage)(one.CurrentItem.CurrentItem as IShellContentController).GetOrCreateContent();
			}
			else
			{
				page = (ShellTestPage)one.CurrentItem.CurrentItem.Content;
			}

			Assert.AreEqual(null, page.SomeQueryParameter);
			await shell.GoToAsync($"//content?{nameof(ShellTestPage.SomeQueryParameter)}=1234");
			Assert.AreEqual("1234", page.SomeQueryParameter);
			await shell.GoToAsync($"//content?{nameof(ShellTestPage.SomeQueryParameter)}=4321");
			Assert.AreEqual("4321", page.SomeQueryParameter);
			await shell.GoToAsync($"//content?{nameof(ShellTestPage.SomeQueryParameter)}");
			Assert.AreEqual(null, page.SomeQueryParameter);
		}

		[Test]
		public async Task DotDotNavigationPassesParameters()
		{
			Routing.RegisterRoute(nameof(DotDotNavigationPassesParameters), typeof(ContentPage));
			var shell = new Shell();
			var one = new ShellItem { Route = "one" };

			var tabone = MakeSimpleShellSection("tabone", "content");

			one.Items.Add(tabone);

			shell.Items.Add(one);

			one.CurrentItem.CurrentItem.ContentTemplate = new DataTemplate(() =>
			{
				ShellTestPage pagetoTest = new ShellTestPage();
				pagetoTest.BindingContext = pagetoTest;
				return pagetoTest;
			});

			var page = (ShellTestPage)(one.CurrentItem.CurrentItem as IShellContentController).GetOrCreateContent();
			Assert.AreEqual(null, page.SomeQueryParameter);
			await shell.GoToAsync(nameof(DotDotNavigationPassesParameters));
			await shell.GoToAsync($"..?{nameof(ShellTestPage.SomeQueryParameter)}=1234");
			Assert.AreEqual("1234", page.SomeQueryParameter);

		}

		[Test]
		public async Task NavigationWithQueryStringWhenPageMatchesBindingContext()
		{
			var shell = new Shell();

			var one = new ShellItem { Route = "one" };
			var two = new ShellItem { Route = "two" };

			var tabone = MakeSimpleShellSection("tabone", "content");
			var tabfour = MakeSimpleShellSection("tabfour", "content", null);

			one.Items.Add(tabone);
			two.Items.Add(tabfour);

			shell.Items.Add(one);
			shell.Items.Add(two);

			ShellTestPage pagetoTest = new ShellTestPage();
			await shell.GoToAsync(new ShellNavigationState($"//two/tabfour/content?{nameof(ShellTestPage.SomeQueryParameter)}=1234"));
			two.CurrentItem.CurrentItem.ContentTemplate = new DataTemplate(() =>
			{
				pagetoTest = new ShellTestPage();
				pagetoTest.BindingContext = pagetoTest;
				return pagetoTest;
			});


			var page = (two.CurrentItem.CurrentItem as IShellContentController).GetOrCreateContent();
			Assert.AreEqual("1234", (page as ShellTestPage).SomeQueryParameter);

		}


		[Test]
		public async Task NavigationWithQueryStringThenWithoutQueryString()
		{
			var shell = new Shell();

			var one = new ShellItem { Route = "one" };
			var two = new ShellItem { Route = "two" };

			var tabone = MakeSimpleShellSection("tabone", "content");
			var tabfour = MakeSimpleShellSection("tabfour", "content", null);

			one.Items.Add(tabone);
			two.Items.Add(tabfour);

			shell.Items.Add(one);
			shell.Items.Add(two);

			ShellTestPage pagetoTest = new ShellTestPage();
			await shell.GoToAsync(new ShellNavigationState($"//two/tabfour/content?{nameof(ShellTestPage.SomeQueryParameter)}=1234"));
			two.CurrentItem.CurrentItem.ContentTemplate = new DataTemplate(() =>
			{
				pagetoTest = new ShellTestPage();
				pagetoTest.BindingContext = pagetoTest;
				return pagetoTest;
			});


			await shell.GoToAsync(new ShellNavigationState($"//one/tabone/content"));
			await shell.GoToAsync(new ShellNavigationState($"//two/tabfour/content"));

			var page = (two.CurrentItem.CurrentItem as IShellContentController).GetOrCreateContent();
			Assert.AreEqual(null, (page as ShellTestPage).SomeQueryParameter);
		}


		[Test]
		public async Task NavigationBetweenShellContentsPassesQueryString()
		{
			var shell = new Shell();

			var item = CreateShellItem(shellSectionRoute: "section2");
			var content = CreateShellContent(shellContentRoute: "content");
			item.Items[0].Items.Add(content);

			Routing.RegisterRoute("details", typeof(ShellTestPage));

			shell.Items.Add(item);


			await shell.GoToAsync(new ShellNavigationState($"//section2/details?{nameof(ShellTestPage.SomeQueryParameter)}=1234"));
			await shell.GoToAsync(new ShellNavigationState($"//content?{nameof(ShellTestPage.SomeQueryParameter)}=1234"));
			await shell.GoToAsync(new ShellNavigationState($"//section2/details?{nameof(ShellTestPage.SomeQueryParameter)}=4321"));

			var testPage = (shell.CurrentItem.CurrentItem as IShellSectionController).PresentedPage as ShellTestPage;
			Assert.AreEqual("4321", testPage.SomeQueryParameter);
		}

		[Test]
		public async Task NavigationBetweenFlyoutItemsRetainsQueryString()
		{
			var testPage1 = new ShellTestPage();
			var testPage2 = new ShellTestPage();

			var flyoutItem1 = CreateShellItem<FlyoutItem>(testPage1, shellItemRoute: "flyoutItem1");
			var flyoutItem2 = CreateShellItem<FlyoutItem>(testPage2, shellItemRoute: "flyoutItem2");

			var shell = new TestShell(flyoutItem1, flyoutItem2);
			var complexParameter = new object();

			IShellController shellController = shell;
			await shell.GoToAsync(new ShellNavigationState($"//flyoutItem2?{nameof(ShellTestPage.SomeQueryParameter)}=1234"), false,
				new Dictionary<string, object>()
				{
					{nameof(ShellTestPage.ComplexObject), complexParameter }
				});

			Assert.AreEqual("1234", testPage2.SomeQueryParameter);
			Assert.AreEqual(complexParameter, testPage2.ComplexObject);
			await shellController.OnFlyoutItemSelectedAsync(flyoutItem1);
			await shellController.OnFlyoutItemSelectedAsync(flyoutItem2);
			Assert.AreEqual("1234", testPage2.SomeQueryParameter);
			Assert.AreEqual(complexParameter, testPage2.ComplexObject);
		}

		[Test]
		public async Task NavigationBetweenFlyoutItemWithPushedPageRetainsQueryString()
		{
			var testPage1 = new ShellTestPage();
			var testPage2 = new ShellTestPage();

			var flyoutItem1 = CreateShellItem<FlyoutItem>(testPage1, shellItemRoute: "flyoutItem1");
			var flyoutItem2 = CreateShellItem<FlyoutItem>(testPage2, shellItemRoute: "flyoutItem2");
			Routing.RegisterRoute("details", typeof(ShellTestPage));


			var shell = new TestShell(flyoutItem1, flyoutItem2);

			IShellController shellController = shell;
			await shell.GoToAsync(new ShellNavigationState($"//flyoutItem2/details?{nameof(ShellTestPage.SomeQueryParameter)}=1234"));

			await shellController.OnFlyoutItemSelectedAsync(flyoutItem1);
			await shellController.OnFlyoutItemSelectedAsync(flyoutItem2);

			var testPage = (shell.CurrentItem.CurrentItem as IShellSectionController).PresentedPage as ShellTestPage;
			Assert.AreEqual("1234", testPage.SomeQueryParameter);
		}

		[Test]
		public async Task BasicQueryStringTest()
		{
			var shell = new Shell();

			var item = CreateShellItem(shellSectionRoute: "section2");
			Routing.RegisterRoute("details", typeof(ShellTestPage));
			shell.Items.Add(item);
			await shell.GoToAsync(new ShellNavigationState($"details?{nameof(ShellTestPage.SomeQueryParameter)}=1234"));
			var testPage = (shell.CurrentItem.CurrentItem as IShellSectionController).PresentedPage as ShellTestPage;
			Assert.AreEqual("1234", testPage.SomeQueryParameter);
		}

		[Test]
		public async Task NavigationWithQueryStringAndNoDataTemplate()
		{
			var shell = new Shell();

			var one = new ShellItem { Route = "one" };
			var two = new ShellItem { Route = "two" };

			var tabone = MakeSimpleShellSection("tabone", "content");
			var tabfour = MakeSimpleShellSection("tabfour", "content");

			one.Items.Add(tabone);
			two.Items.Add(tabfour);

			shell.Items.Add(one);
			shell.Items.Add(two);

			await shell.GoToAsync(new ShellNavigationState($"//two/tabfour/content?{nameof(ShellTestPage.SomeQueryParameter)}=1234"));
			Assert.AreEqual("1234", (two.CurrentItem.CurrentItem.Content as ShellTestPage).SomeQueryParameter);
		}


		[Test]
		public async Task UriDecodesQueryString()
		{
			var shell = new Shell();
			var item = CreateShellItem();
			Routing.RegisterRoute("details", typeof(ShellTestPage));
			shell.Items.Add(item);

			await shell.GoToAsync(new ShellNavigationState($"details?{nameof(ShellTestPage.SomeQueryParameter)}=1 2 3 4 % ^"));
			var testPage = shell.CurrentPage as ShellTestPage;
			Assert.AreEqual("1 2 3 4 % ^", testPage.SomeQueryParameter);
		}

		[Test]
		public async Task UriEncodedStringDecodesCorrectly()
		{
			var shell = new Shell();
			var item = CreateShellItem();
			Routing.RegisterRoute("details", typeof(ShellTestPage));
			shell.Items.Add(item);

			string result = "http://www.place.com 1 2 3 4 % ()(";
			string parameter = System.Net.WebUtility.UrlEncode(result);

			await shell.GoToAsync(new ShellNavigationState($"details?{nameof(ShellTestPage.SomeQueryParameter)}={parameter}"));
			var testPage = shell.CurrentPage as ShellTestPage;
			Assert.AreEqual(result, testPage.SomeQueryParameter);
		}

		[Test]
		public async Task UrlParameter()
		{
			var shell = new Shell();
			var item = CreateShellItem();
			Routing.RegisterRoute("details", typeof(ShellTestPage));
			shell.Items.Add(item);

			string urlTest = @"https://www.somewebsite.com/id/545/800/600.jpg";
			await shell.GoToAsync(new ShellNavigationState($"details?{nameof(ShellTestPage.SomeQueryParameter)}={urlTest}"));
			var testPage = shell.CurrentPage as ShellTestPage;
			Assert.AreEqual(urlTest, testPage.SomeQueryParameter);
		}

		[Test]
		public async Task SetParameterOfTypeThatsNotAString()
		{
			var shell = new Shell();
			var item = CreateShellItem();
			Routing.RegisterRoute("details", typeof(ShellTestPage));
			shell.Items.Add(item);
			await shell.GoToAsync(new ShellNavigationState($"details?{nameof(ShellTestPage.DoubleQueryParameter)}=1234"));
			var testPage = (shell.CurrentItem.CurrentItem as IShellSectionController).PresentedPage as ShellTestPage;
			Assert.AreEqual(1234d, testPage.DoubleQueryParameter);
		}

		[Test]
		public async Task NavigatingBackDoesntClearParametersFromPreviousPage()
		{
			var shell = new TestShell(CreateShellItem());

			Routing.RegisterRoute("details", typeof(ShellTestPage));

			await shell.GoToAsync($"details?{nameof(ShellTestPage.SomeQueryParameter)}=1");
			await shell.GoToAsync("details");
			await shell.GoToAsync("..");
			var testPage = shell.CurrentPage as ShellTestPage;
			Assert.AreEqual("1", testPage.SomeQueryParameter);

		}

		[Test]
		public async Task NavigatingBackAbsolutelyClearsParametersFromPreviousPage()
		{
			var shell = new TestShell(CreateShellItem(shellItemRoute: "item"));

			Routing.RegisterRoute("details", typeof(ShellTestPage));

			await shell.GoToAsync($"details?{nameof(ShellTestPage.SomeQueryParameter)}=1");
			await shell.GoToAsync("details");
			await shell.GoToAsync("//item/details");
			var testPage = shell.CurrentPage as ShellTestPage;
			Assert.AreEqual(null, testPage.SomeQueryParameter);

		}

		[Test]
		public async Task NavigatingBackToShellContentRetainsQueryParameter()
		{
			var shell = new Shell();
			ShellTestPage pagetoTest = new ShellTestPage();
			pagetoTest.BindingContext = pagetoTest;
			var one = CreateShellItem(pagetoTest, shellContentRoute: "content");
			shell.Items.Add(one);
			ShellTestPage page = (ShellTestPage)shell.CurrentPage;
			await shell.GoToAsync($"//content?{nameof(ShellTestPage.SomeQueryParameter)}=1234");
			await shell.Navigation.PushAsync(new ContentPage());
			await shell.Navigation.PopAsync();
			Assert.AreEqual("1234", page.SomeQueryParameter);
		}

		[Test]
		public async Task NavigatingBackToShellContentAbsolutelyClearsQueryParameter()
		{
			var shell = new Shell();
			ShellTestPage pagetoTest = new ShellTestPage();
			pagetoTest.BindingContext = pagetoTest;
			var one = CreateShellItem(pagetoTest, shellContentRoute: "content");
			shell.Items.Add(one);
			ShellTestPage page = (ShellTestPage)shell.CurrentPage;
			await shell.GoToAsync($"//content?{nameof(ShellTestPage.SomeQueryParameter)}=1234");
			await shell.Navigation.PushAsync(new ContentPage());
			await shell.GoToAsync($"//content");
			Assert.AreEqual(null, page.SomeQueryParameter);
		}

		[Test]
		public async Task BasicShellParameterTest()
		{
			var shell = new Shell();

			var item = CreateShellItem(shellSectionRoute: "section2");
			Routing.RegisterRoute("details", typeof(ShellTestPage));
			shell.Items.Add(item);
			var obj = new object();
			var parameter = new ShellRouteParameters
			{
				{"DoubleQueryParameter", 2d },
				{ "ComplexObject", obj}
			};

			await shell.GoToAsync(new ShellNavigationState($"details?{nameof(ShellTestPage.SomeQueryParameter)}=1234"), parameter);
			var testPage = (shell.CurrentItem.CurrentItem as IShellSectionController).PresentedPage as ShellTestPage;
			Assert.AreEqual("1234", testPage.SomeQueryParameter);
			Assert.AreEqual(2d, testPage.DoubleQueryParameter);
			Assert.AreEqual(obj, testPage.ComplexObject);
		}

		[Test]
		public async Task DotDotNavigationPassesShellParameters()
		{
			Routing.RegisterRoute(nameof(DotDotNavigationPassesParameters), typeof(ContentPage));
			var shell = new Shell();
			var one = new ShellItem { Route = "one" };

			var tabone = MakeSimpleShellSection("tabone", "content");

			one.Items.Add(tabone);

			shell.Items.Add(one);

			one.CurrentItem.CurrentItem.ContentTemplate = new DataTemplate(() =>
			{
				ShellTestPage pagetoTest = new ShellTestPage();
				pagetoTest.BindingContext = pagetoTest;
				return pagetoTest;
			});

			var obj = new object();
			var parameter = new ShellRouteParameters
			{
				{"DoubleQueryParameter", 2d },
				{ "ComplexObject", obj}
			};

			var page = (ShellTestPage)(one.CurrentItem.CurrentItem as IShellContentController).GetOrCreateContent();
			Assert.AreEqual(null, page.SomeQueryParameter);
			await shell.GoToAsync(nameof(DotDotNavigationPassesParameters));
			await shell.GoToAsync($"..?{nameof(ShellTestPage.SomeQueryParameter)}=1234", parameter);
			Assert.AreEqual("1234", page.SomeQueryParameter);
			Assert.AreEqual(2d, page.DoubleQueryParameter);
			Assert.AreEqual(obj, page.ComplexObject);
		}

		[Test]
		public async Task PassesUrlInShellParameter()
		{
			var shell = new Shell();
			var item = CreateShellItem();
			Routing.RegisterRoute("details", typeof(ShellTestPage));
			shell.Items.Add(item);
			string urlTest = @"https://www.somewebsite.com/id/545/800/600.jpg";

			var parameter = new ShellRouteParameters
			{
				{ nameof(ShellTestPage.SomeQueryParameter) ,urlTest }
			};

			await shell.GoToAsync(new ShellNavigationState($"details"), parameter);
			var testPage = shell.CurrentPage as ShellTestPage;
			Assert.AreEqual(urlTest, testPage.SomeQueryParameter);
		}

		[Test]
		public void InitialNavigationDoesntSetQueryAttributesProperty()
		{
			var content = CreateShellContent();
			_ = new TestShell(content);
			Assert.IsFalse(content.IsSet(ShellContent.QueryAttributesProperty));
		}
	}
}
