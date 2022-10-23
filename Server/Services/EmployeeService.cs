﻿using Grpc.Core;
using DotNetEnv;
using Npgsql;
using Google.Protobuf.WellKnownTypes;
using System.Security.Cryptography;
using System.Text;

namespace Server.Services
{
    public class EmployeeService : Employee.EmployeeBase
    {
        /// <summary>
        /// Method <c>GetHashedString</c> takes a string input and returns the base64 encoded hashed string.
        /// </summary>
        /// <param name="input">The plaintext string to be hashed</param>
        /// <returns>The base64 encoded string after it has been hashed</returns>
        private static string GetHashedString(string input)
        {
            using (var sha512 = SHA512.Create())
            {
                var hashedBytes = sha512.ComputeHash(Encoding.UTF8.GetBytes(input));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        /// <summary>
        /// Method <c>GetConnection</c> creates a connection to a PostgreSQL Database.
        /// </summary>
        /// <returns>A connection to the PostgreSQL database using database credentials</returns>
        private static NpgsqlConnection GetConnection()
        {
            Env.TraversePath().Load();
            string? DB_HOST = Environment.GetEnvironmentVariable("HOST");
            string? DB_PORT = Environment.GetEnvironmentVariable("PORT");
            string? DB_NAME = Environment.GetEnvironmentVariable("DATABASE");
            string? DB_USERNAME = Environment.GetEnvironmentVariable("USERNAME");
            string? DB_PASSWORD = Environment.GetEnvironmentVariable("PASSWORD");
            string connectionString = "Server=" + DB_HOST + ";Port=" + DB_PORT + ";Database=" + DB_NAME + ";User Id=" + DB_USERNAME + ";Password=" + DB_PASSWORD;
            return new NpgsqlConnection(@connectionString);
        }

        /// <summary>
        /// Implementation of the newEmployee RPC for adding a new employee
        /// </summary>
        /// <param name="request">An object containing the first name, last name, username, and password for the new employee.</param>
        /// <param name="context"></param>
        /// <returns><c>true</c> if the operation was successful. <c>false</c> otherwise.</returns>
        public override Task<ServiceStatus> newEmployee(EmployeeInfo request, ServerCallContext context)
        {
            ServiceStatus status = new();
            status.IsSuccessfulOperation = CreateNewEmployee(request);
            return Task.FromResult(status);
        }

        /// <summary>
        /// Method <c>CreateNewEmployee</c> inserts a new record into Employees table in the database
        /// </summary>
        /// <param name="employeeInfo">An object containing the first name, last name, username, and password of the new employee</param>
        /// <returns><c>true</c> if the INSERT operation was successful. <c>false</c> otherwise.</returns>
        private static bool CreateNewEmployee(EmployeeInfo employeeInfo)
        {
            NpgsqlCommand command;
            string query;
            int status;

            using (NpgsqlConnection conn = GetConnection())
            {
                query = "INSERT INTO Employees (first_name, last_name, employee_username, employee_password) VALUES ($1, $2, $3, $4);";
                command = new NpgsqlCommand(@query, conn)
                {
                    Parameters =
                    {
                        new() {Value = employeeInfo.FirstName},
                        new() {Value = employeeInfo.LastName},
                        new() {Value = employeeInfo.Credentials.Username},
                        new() {Value = GetHashedString(employeeInfo.Credentials.Password)}
                    }
                };
                conn.Open();
                status = command.ExecuteNonQuery();
                conn.Close();
            }
            return status == 1;                                             // INSERT returns the number of rows affected. This operation expects 1 to be successful.
        }

        /// <summary>
        /// Implementation of the updateEmployee RPC for updating information about an employee.
        /// </summary>
        /// <param name="request">An object containing the updated employee's first name, last name, username, and password - all of them (or none) can be changed as needed</param>
        /// <param name="context"></param>
        /// <returns><c>true</c> if the operation was successful. <c>false</c> otherwise.</returns>
        public override Task<ServiceStatus> updateEmployee(EmployeeInfo request, ServerCallContext context)
        {
            ServiceStatus status = new();
            status.IsSuccessfulOperation = UpdateEmployeeRecord(request);
            return Task.FromResult(status);
        }

        /// <summary>
        /// Method <c>UpdateEmployeeRecord</c> takes in an object of EmployeeInfo and executes a UPDATE to the database to change the record of a specific employee.
        /// </summary>
        /// <param name="info">An object containing the employee's id, first name, last name, username, and password. Any of these fields, except for ID, can be changed.</param>
        /// <returns><c>true</c> if the UPDATE operation was successful. <c>false</c> otherwise.</returns>
        private static bool UpdateEmployeeRecord(EmployeeInfo info)
        {
            NpgsqlCommand command;
            NpgsqlDataReader reader;
            string query;
            int status;
            bool needUpdate = true;

            using (NpgsqlConnection conn = GetConnection())
            {
                /*
                 * Check if an update is necessary. 
                 * If all the information sent from the Client is the same as all the data currently stored, UPDATE is not necessary.
                 */
                query = "SELECT * FROM Employees WHERE employee_id = $1;";
                command = new NpgsqlCommand(@query, conn)
                {
                    Parameters =
                    {
                        new() {Value = info.EmployeeId}
                    }
                };
                conn.Open();
                reader = command.ExecuteReader();
                if (reader.HasRows)
                {
                    reader.Read();
                    needUpdate = !((reader["first_name"].ToString()).Equals(info.FirstName) &&
                        (reader["last_name"].ToString()).Equals(info.LastName) &&
                        (reader["first_name"].ToString()).Equals(info.FirstName) &&
                        (reader["employee_username"].ToString()).Equals(info.Credentials.Username) &&
                        (reader["employee_password"].ToString()).Equals(GetHashedString(info.Credentials.Password)));
                }
                conn.Close();

                // if some information received from the Client side did not match what was stored in the database, update the database.
                if (needUpdate)
                {
                    query = "UPDATE Employees SET " +
                        "first_name = $1, " +
                        "last_name = $2, " +
                        "employee_username = $3, " +
                        "employee_password = $4 " +
                        "WHERE employee_id = $5;";
                    command = new NpgsqlCommand(@query, conn)
                    {
                        Parameters =
                    {
                        new() {Value = info.FirstName},
                        new() {Value = info.LastName},
                        new() {Value = info.Credentials.Username},
                        new() {Value = GetHashedString(info.Credentials.Password)},
                        new() {Value = info.EmployeeId}
                    }
                    };
                    conn.Open();
                    status = command.ExecuteNonQuery();
                    conn.Close();
                    return status == 1;                                             // UPDATE returns the number of rows affected. This operation expects 1 to be considered successful.
                } else
                {
                    return true;                                                    // No UPDATE was needed. The operation would be considered successful.
                }
            }
        } 

        /// <summary>
        /// Implementation of the getEmployees RPC for getting information about all employees stored in the database.
        /// </summary>
        /// <param name="request">Empty. This RPC call does not require input parameters.</param>
        /// <param name="context"></param>
        /// <returns>An object containing any number of objects where each of the sub-objects represents information about an Employee record in the database</returns>
        public override Task<AllEmployeesInfo> getEmployees(Empty request, ServerCallContext context)
        {
            return Task.FromResult(SelectAllEmployees());
        }

        /// <summary>
        /// Method <c>SelectAllEmployees</c> selects all rows from the Employee table and packs it into a message to be sent back.
        /// </summary>
        /// <returns>A Protobuf message containing information about every Employee stored in the database</returns>
        private static AllEmployeesInfo SelectAllEmployees()
        {
            AllEmployeesInfo allEmployees = new();
            NpgsqlCommand command;
            NpgsqlDataReader reader;
            string query;

            using (NpgsqlConnection conn = GetConnection())
            {
                query = "SELECT * FROM Employees;";
                command = new NpgsqlCommand(@query, conn);
                conn.Open();
                reader = command.ExecuteReader();
                if(reader.HasRows)
                {
                    while(reader.Read())
                    {
                        var currentCredentials = new LoginCredentials
                        {
                            Username = reader["employee_username"].ToString(),
                            Password = reader["employee_password"].ToString()
                        };
                        var current = new EmployeeInfo
                        {
                            EmployeeId = Convert.ToInt32(reader["employee_id"]),
                            FirstName = reader["first_name"].ToString(),
                            LastName = reader["last_name"].ToString(),
                            Credentials = currentCredentials
                        };
                        allEmployees.Employees.Add(current);
                    }
                }
                reader.Close();
                conn.Close();
            }
            return allEmployees;
        }

        /// <summary>
        /// Implementation of the doLogin RPC for verifying login credentials.
        /// </summary>
        /// <param name="request">An object containing the username and password being verified for login.</param>
        /// <param name="context"></param>
        /// <returns><c>true</c> if the credentials are valid. <c>false</c> otherwise.</returns>
        public override Task<LoginStatus> doLogin(LoginCredentials request, ServerCallContext context)
        {
            LoginStatus status = new();
            status.IsSuccessfulLogin = CheckCredentials(request.Username, request.Password);
            return Task.FromResult(status);
        }

        /// <summary>
        /// Method <c>CheckCredentials</c> queries the database for the password given the username.
        /// </summary>
        /// <param name="username">The username of the account</param>
        /// <param name="password">The user entered password to be verified against what is already in the database</param>
        /// <returns><c>true</c> if <c>password</c> matches what is stored in the database. <c>false</c> otherwise</returns>
        private static bool CheckCredentials(string username, string password)
        {
            NpgsqlCommand command;
            string query;
            string? expectedPassword;

            using (NpgsqlConnection conn = GetConnection())
            {
                query = "SELECT employee_password FROM Employees WHERE employee_username = $1;";
                command = new NpgsqlCommand(@query, conn)
                {
                    Parameters =
                    {
                        new() {Value = username}
                    }
                };
                conn.Open();
                expectedPassword = (command.ExecuteScalar() == null) ? string.Empty : command.ExecuteScalar().ToString();
                conn.Close();
            }
            return GetHashedString(password).Equals(expectedPassword);
        }
    }
}