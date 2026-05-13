using System.ComponentModel.DataAnnotations;

namespace WEBchat.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập Username.")]
        public string Username { get; set; } = null!;

        [Required(ErrorMessage = "Vui lòng nhập Password.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = null!;
    }

    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập Username.")]
        [StringLength(20, MinimumLength = 3, ErrorMessage = "Tên đăng nhập từ 3 - 20 kí tự.")]
        public string Username { get; set; } = null!;

        [Required(ErrorMessage = "Vui lòng nhập Password.")]
        [DataType(DataType.Password)]
        [StringLength(50, MinimumLength = 6, ErrorMessage = "Mật khẩu tối thiểu 6 kí tự.")]
        public string Password { get; set; } = null!;

        [Required(ErrorMessage = "Vui lòng xác nhận Password.")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Mật khẩu xác nhận không khớp.")]
        public string ConfirmPassword { get; set; } = null!;

        [Required(ErrorMessage = "Vui lòng nhập Tên hiển thị.")]
        public string DisplayName { get; set; } = null!;

        [Required(ErrorMessage = "Vui lòng nhập Tuổi.")]
        [Range(1, 150, ErrorMessage = "Tuổi không hợp lệ.")]
        public int Age { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập Email.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        public string Email { get; set; } = null!;

        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        public string? Phone { get; set; }
    }
}
