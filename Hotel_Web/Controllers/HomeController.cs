using Hotel_Web.Models;
using PagedList;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Web.Mvc;


namespace Hotel_Web.Controllers
{
    public class HomeController : Controller
    {
        SaksHotelEntities db = new SaksHotelEntities();
        private void SetAdminCookie(string uid)
        {
            Response.Cookies["AdminUid"].Value = uid;
            Response.Cookies["AdminUid"].Expires = DateTime.Now.AddDays(1);
        }

        private Employee GetCurrentAdminFromCookie()
        {
            var userCookie = Request.Cookies["AdminUid"];
            if (userCookie != null)
            {
                var uid = userCookie.Value;
                return db.Employee.SingleOrDefault(u => u.Username == uid); ;
            }
            else
            {
                return null;
            }
        }

        private async Task<StaticPagedList<T>> GetPagedResultAsync<T>(IQueryable<T> query, int pageNumber, int pageSize)
        {
            try
            {
                var totalCount = await query.CountAsync();
                if (totalCount == 0) // Check whether the query result is empty
                {
                    return new StaticPagedList<T>(new List<T>(), 1, pageSize, totalCount);
                }
                var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
                return new StaticPagedList<T>(items, pageNumber, pageSize, totalCount);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null; // Return null or other error message
            }
        }



        public ActionResult AdminLogin()
        {
            return View();
        }

        [HttpPost]
        public ActionResult AdminLogin(string Adminname, string AdminPassword, bool RememberMe = false)
        {
            var user = db.Employee.FirstOrDefault(u => u.Username == Adminname && u.Password == AdminPassword);
            if (user == null)
            {
                ViewBag.ErrorMsg = "The user name or password is incorrect";
                return View();
            }

            SetAdminCookie(Adminname);
            return RedirectToAction("AdminIndex", "Home");
        }

        public async Task<ActionResult> AdminIndex(int? page, string Type, string Check)
        {
            var customers = db.Customer.Include(c => c.UserInfo).Where(c => c.EmployeeID == null && c.RoomID == null);
            if (!string.IsNullOrEmpty(Check))
            {
                switch (Type)
                {
                    case "姓名":
                        customers = customers.Where(c => c.UserInfo.Username.Contains(Check));
                        break;
                    case "身份证":
                        customers = customers.Where(c => c.UserInfo.IDCardNumber.Contains(Check));
                        break;
                    default:
                        break;
                }
            }

            var pagedCustomers = await GetPagedResultAsync(customers.OrderBy(c => c.CustomerID), page ?? 1, 6);
            ViewBag.CurrentUser = pagedCustomers;
            return View(ViewBag.CurrentUser);
        }

        public ActionResult Room(int? page, int? pageSize, string roomType)
        {
            var pageNumber = page ?? 1;
            var size = pageSize ?? 6;
            var rooms = db.Room.Include(r => r.RoomType);

            if (!string.IsNullOrEmpty(roomType))
            {
                rooms = rooms.Where(r => r.RoomType.Type == roomType);
            }

            var orderedRooms = rooms.OrderBy(r => r.RoomType.TypeID).ThenBy(r => r.RoomID);
            var pagedRooms = orderedRooms.ToPagedList(pageNumber, size);
            ViewBag.PageCount = pagedRooms.PageCount;
            ViewBag.CurrentPage = pagedRooms.PageNumber;
            ViewBag.Room = pagedRooms;

            // Set RoomTypes to properties in the ViewBag
            ViewBag.RoomTypes = db.RoomType.Select(r => r.Type).Distinct().ToList();

            return View();
        }



        public ActionResult UpdateRoom(int? RoomID)
        {
            var room = db.Room.Include("RoomType").SingleOrDefault(r => r.RoomID == RoomID);
            if (room == null)
            {
                // If the corresponding room cannot be found, it redirects to an error page or default page.
                return RedirectToAction("Room", "Home");
            }
            ViewBag.Room = room;
            ViewBag.Types = db.RoomType.ToList(); // Store all room types in the ViewBag
            return View();
        }

        [HttpPost]
        public ActionResult UpdateRoom(int? RoomID, int TypeID)
        {
            var room = db.Room.Include("RoomType").SingleOrDefault(r => r.RoomID == RoomID);
            room.TypeID = TypeID;
            db.SaveChanges();
            return RedirectToAction("Room");
        }

        public ActionResult AddRoom()
        {
            ViewBag.Types = db.RoomType.ToList(); // Store all room types in the ViewBag
            return View();
        }

        [HttpPost]
        public ActionResult AddRoom(int? TypeID)
        {
            var room = new Room
            {
                TypeID = TypeID,
                Status = "vacant"
            };
            db.Room.Add(room);
            db.SaveChanges();
            return RedirectToAction("Room");
        }

        public ActionResult Historical(int? page, int? pageSize)
        {
            var pageNumber = page ?? 1;
            var size = pageSize ?? 6;

            var historical = db.Transactions.OrderBy(t => t.TransactionDate);
            var pagedHistorical = historical.ToPagedList(pageNumber, size);
            ViewBag.PageCount = pagedHistorical.PageCount;
            ViewBag.CurrentPage = pagedHistorical.PageNumber;
            ViewBag.historical = pagedHistorical;
            return View();
        }


        public ActionResult Transactions(int? page, int? pageSize, string searchName)
        {
            var pageNumber = page ?? 1;
            var size = pageSize ?? 10;

            // Construct search criteria for surname matching
            Expression<Func<Transactions, bool>> namePredicate;
            if (!string.IsNullOrEmpty(searchName))
            {
                namePredicate = t => t.Customer.UserInfo.Username.StartsWith(searchName);
            }
            else
            {
                namePredicate = t => true;
            }

            // Check all check-out records
            var checkOuts = db.Transactions.Where(t => t.Type == "Check out")
                                           .OrderByDescending(t => t.TransactionDate)
                                           .Select(t => t.CustomerID)
                                           .ToList();

            // Exclude check-in records that have already checked out and filter results by name
            var checkIns = db.Transactions.Where(t => t.Type == "Check in" && !checkOuts.Contains(t.CustomerID))
                                          .Where(namePredicate)
                                          .OrderBy(t => t.TransactionID)
                                          .ToPagedList(pageNumber, size);

            ViewBag.CurrentPage = checkIns.PageNumber;
            ViewBag.Transactions = checkIns;
            ViewBag.PageCount = checkIns.PageCount; // Add total number of pages

            return View();
        }

        public ActionResult Stay(int? CustomerID)
        {
            if (CustomerID == null)
            {
                return RedirectToAction("Index");
            }
            var customer = db.Customer.SingleOrDefault(c => c.CustomerID == CustomerID);
            var currentUser = GetCurrentAdminFromCookie();
            if (customer == null)
            {
                return HttpNotFound();
            }
            ViewBag.User = currentUser;
            ViewBag.Customer = customer;
            return View();
        }

        [HttpPost]
        public ActionResult Stay(DateTime? CheckOutDate, int? CustomerID)
        {
            if (CustomerID == null)
            {
                return RedirectToAction("Error");
            }

            using (var db = new SaksHotelEntities())
            {
                var customer = db.Customer.FirstOrDefault(c => c.CustomerID == CustomerID);
                if (customer != null)
                {
                    customer.CheckOutDate = CheckOutDate;
                    db.SaveChanges();
                }
            }

            return RedirectToAction("Transactions");
        }



        public ActionResult Checkout(int? TransactionID)
        {
            // Obtain the corresponding transaction information based on customerId
            var transaction = db.Transactions.SingleOrDefault(t => t.TransactionID == TransactionID);

            if (transaction != null)
            {
                // Gets the customer ID in the transaction history
                var customerID = transaction.CustomerID;

                // Create a new transaction record
                var newTransaction = new Transactions
                {
                    CustomerID = customerID,
                    Type = "Check out",
                    TransactionDate = DateTime.Now,
                    Employee = GetCurrentAdminFromCookie()
                };

                // Update the trade list
                db.Transactions.Add(newTransaction);
                db.SaveChanges();

                // Get the room number of the check-out guest
                var roomNumber = transaction.Customer.RoomID;

                // Get room information according to the room number
                var room = db.Room.SingleOrDefault(r => r.RoomID == roomNumber);

                if (room != null)
                {
                    // Update the room status to "vacant"
                    room.Status = "vacant";

                    // Save changes to transactions and rooms
                    db.SaveChanges();
                }
            }

            return Json(new { success = true });
        }



        public async Task<ActionResult> CheckIn(int? CustomerID)
        {

            // Get the current user information
            var currentUser = GetCurrentAdminFromCookie();

            var customer = await db.Customer.Include(c => c.RoomType).SingleOrDefaultAsync(c => c.CustomerID == CustomerID && c.EmployeeID == null && c.RoomID == null);
            if (customer == null)
            {
                ViewBag.ErrorMsg = "Customer does not exist or has been checked in";
                return View("Error");
            }

            var roomTypeID = customer.RoomTypeID;
            var rooms = await db.Room.Where(r => r.TypeID == roomTypeID && r.Status == "vacant").ToListAsync();
            ViewBag.Current = customer;
            ViewBag.RoomList = rooms;
            ViewBag.User = currentUser;

            return View();
        }

        [HttpPost]
        public ActionResult CheckIn(int RoomID, int EmployeeID, int CustomerID)
        {
            // Find the Customer record that needs to be modified
            var customer = db.Customer.FirstOrDefault(c => c.CustomerID == CustomerID);
            if (customer != null)
            {
                // Update the RoomID and EmployeeID
                customer.RoomID = RoomID;
                customer.EmployeeID = EmployeeID;

                // Update the Room status of the room table
                var room = db.Room.Find(RoomID);
                room.Status = "checked in";

                // Add a new Transaction record
                var Transactions = new Transactions()
                {
                    Type = "Check in",
                    TransactionDate = DateTime.Now,  // time
                    EmployeeID = EmployeeID,
                    CustomerID = CustomerID
                };
                db.Transactions.Add(Transactions);

                // Commit changes
                db.SaveChanges();

                // Pop-up prompt
                TempData["Message"] = "Check-in success!";
                TempData["MessageType"] = "success";
            }

            // Return result view
            return RedirectToAction("AdminIndex");
        }

        [HttpPost]
        public JsonResult DeleteData(int id)
        {
            try
            {
                // Delete the corresponding data in the database based on the ID
                var customer = db.Customer.FirstOrDefault(c => c.CustomerID == id);
                if (customer != null)
                {
                    db.Customer.Remove(customer);
                    db.SaveChanges();
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return Json(new { success = false, message = ex.Message });
            }
        }



        public ActionResult Login()
        {
            return View();
        }

        private void SetUserCookie(string uid)
        {
            Response.Cookies["uid"].Value = uid;
            Response.Cookies["uid"].Expires = DateTime.Now.AddDays(1);
        }

        private UserInfo GetCurrentUserFromCookie()
        {
            var userCookie = Request.Cookies["uid"];
            if (userCookie != null)
            {
                var uid = userCookie.Value;
                return db.UserInfo.SingleOrDefault(u => u.Username == uid);
            }
            else
            {
                return null;
            }
        }

        [HttpPost]
        public ActionResult Login(string Username, string Password, bool RememberMe = false)
        {
            var user = db.UserInfo.FirstOrDefault(u => u.Username == Username && u.Password == Password);
            if (user == null)
            {
                ViewBag.ErrorMsg = "The user name or password is incorrect";
                return View();
            }

            SetUserCookie(Username);
            return RedirectToAction("Index", "Home");
        }

        public ActionResult SignUp()
        {
            return View();
        }

        // Add the SignUp method to the HomeController to handle the registration information submitted by the user
        [HttpPost]
        public ActionResult SignUp(UserInfo model)
        {
            // Verify that the data entered by the user is legitimate
            if (ModelState.IsValid)
            {
                // If the validation is successful, the data entered by the user is added to the database
                using (var db = new SaksHotelEntities())
                {
                    var user = new UserInfo()
                    {
                        Username = model.Username,
                        Password = model.Password,
                        IDCardNumber = model.IDCardNumber,
                        PhoneNumber = model.PhoneNumber
                    };
                    db.UserInfo.Add(user);
                    db.SaveChanges();
                    return RedirectToAction("Login", "Home");
                }
            }

            // If the verification fails, the registration page is returned with an error message
            return View();
        }

        public async Task<ActionResult> Index()
        {
            var currentUser = GetCurrentUserFromCookie();
            var groupedRooms = await db.Room.Where(room => room.Status == "vacant")
                                             .GroupBy(room => room.TypeID)
                                             .ToListAsync();
            var distinctRooms = groupedRooms.Select(grp => grp.FirstOrDefault());
            ViewBag.Rooms = distinctRooms;
            return View();
        }

        public ActionResult Pay(DateTime ChecklnDate, DateTime checkoutDate, int roomTypeID)
        {
            var currentUser = GetCurrentUserFromCookie();
            ViewBag.ChecklnDate = ChecklnDate;
            ViewBag.checkoutDate = checkoutDate;

            var roomType = db.RoomType.SingleOrDefault(rt => rt.TypeID == roomTypeID);
            if (roomType == null)
            {
                // Room type does not exist, error page returned
                ViewBag.ErrorMsg = "Room type does not exist";
                return View("Error");
            }

            ViewBag.roomType = roomType;
            ViewBag.CurrentUser = currentUser;

            return View();
        }

        [HttpPost]
        public async Task<ActionResult> Pay(DateTime? ChecklnDate, DateTime? checkoutDate, int? roomTypeID, string iDCardNumber, int totalPrice)
        {
            if (!ChecklnDate.HasValue || !checkoutDate.HasValue || !roomTypeID.HasValue || string.IsNullOrEmpty(iDCardNumber))
            {
                // Parameter is incomplete, processing error
                ViewBag.ErrorMsg = "Parameter incompleteness";
                return View("Error");
            }

            var currentUser = GetCurrentUserFromCookie();
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var room = await db.Room.FirstOrDefaultAsync(r => r.RoomType.TypeID == roomTypeID && r.Status == "vacant");
            if (room == null)
            {
                // If no available room is found, go to the error page or re-select the room type
                ViewBag.ErrorMsg = "This type of room is full, please choose another type of room";
                return View("Error");
            }

            var customer = new Models.Customer
            {
                CheckInID = iDCardNumber,
                ChecklnDate = ChecklnDate,
                CheckOutDate = checkoutDate,
                RoomTypeID = roomTypeID.Value,
                RoomID = null,
                EmployeeID = null,
                UserID = currentUser.UserID,
                TotalPrice = totalPrice
            };
            db.Customer.Add(customer);
            await db.SaveChangesAsync();

            return RedirectToAction("BookingSuccess", new { customerId = customer.CustomerID });
        }

        public ActionResult BookingSuccess(int? customerId)
        {
            if (!customerId.HasValue)
            {
                // If the order ID is empty, a processing error occurs
                ViewBag.ErrorMsg = "Invalid order ID";
                return View("Error");
            }

            var customer = db.Customer.Include(c => c.RoomType).FirstOrDefault(c => c.CustomerID == customerId.Value);
            if (customer == null)
            {
                // If no order is found, processing error
                ViewBag.ErrorMsg = "Invalid order ID";
                return View("Error");
            }

            return View(customer);
        }

        public async Task<ActionResult> OrderRecord(int page = 1, int pageSize = 12)
        {
            // Get current user
            UserInfo currentUser = GetCurrentUserFromCookie();

            // Gets all order records for the user
            var orders = db.Customer.Include(c => c.RoomType).Where(c => c.UserID == currentUser.UserID);

            // Paging method
            var pagedOrders = await GetPagedResultAsync(orders.OrderByDescending(c => c.CustomerID), page, pageSize);

            // Store the calculated values in the ViewBag for the view to use
            ViewBag.OrderRecord = pagedOrders;

            // Add paging navigation
            ViewBag.PageCount = pagedOrders.PageCount;
            ViewBag.CurrentPage = pagedOrders.PageNumber;

            // Return view
            return View();
        }

        public ActionResult Logout()
        {
            Response.Cookies["uid"].Expires = DateTime.Now.AddDays(-1);
            return RedirectToAction("Login", "Home");
        }
    }
}
