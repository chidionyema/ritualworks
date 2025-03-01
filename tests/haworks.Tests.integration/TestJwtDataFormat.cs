// Add this in your test project's Infrastructure folder
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Haworks.Tests
{
public class TestJwtDataFormat : ISecureDataFormat<AuthenticationTicket>
{
    private readonly string _signingKey;

    public TestJwtDataFormat(string signingKey)
    {
        _signingKey = signingKey;
    }

    public string Protect(AuthenticationTicket data) => Protect(data, null);

    public string Protect(AuthenticationTicket data, string purpose)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey));
        
        var token = new JwtSecurityToken(
            issuer: "TestIssuer",
            audience: "TestAudience",
            claims: data.Principal.Claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return handler.WriteToken(token);
    }

    public AuthenticationTicket Unprotect(string protectedText) => Unprotect(protectedText, null);

    public AuthenticationTicket Unprotect(string protectedText, string purpose)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(protectedText);
        
        var identity = new ClaimsIdentity(token.Claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        
        return new AuthenticationTicket(principal, "Test");
    }
}

public class TestDataProtector : IDataProtector
{
    public IDataProtector CreateProtector(string purpose) => this;
    public byte[] Protect(byte[] plaintext) => plaintext;
    public byte[] Unprotect(byte[] protectedData) => protectedData;
}
}