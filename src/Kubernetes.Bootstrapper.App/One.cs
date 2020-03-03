using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kubernetes.Bootstrapper.App
{
    [Route("One")]
    public class OneController : Controller
    {
        [HttpGet]
        public void Get()
        {
        }
    }
}
