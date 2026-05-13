const connection = new signalR.HubConnectionBuilder()
    .withUrl("/chatHub")
    .withAutomaticReconnect()
    .build();
console.log("WebChat: chat.js đã được nạp thành công.");

let activeConversationId = null;
let currentConversationDetails = null; // Store fetched data about active chat

// Initialize
connection.start().then(function () {
    console.log("SignalR Connected.");
    loadConversations();
    loadUsers();
}).catch(function (err) {
    return console.error(err.toString());
});

// Receive Message
connection.on("ReceiveMessage", function (message) {
    if (message.conversationId === activeConversationId) {
        appendMessage(message);
        scrollToBottom();
    }
    // Update conversation list
    loadConversations(); 
});

connection.on("UpdateConversationList", function () {
    console.log("WebChat: Cập nhật danh sách hội thoại...");
    loadConversations();
});

window.backToSidebar = function() {
    document.getElementById("chatArea").classList.remove("active");
}

// Receive Friend Request Notification
connection.on("ReceiveFriendRequest", function (requesterName) {
    document.getElementById("friendRequestBadge").classList.remove("d-none");
    // Optionally show a toast or alert
    console.log("Bạn có lời mời kết bạn mới từ " + requesterName);
});

function loadConversations() {
    fetch('/api/chatapi/conversations')
        .then(res => res.json())
        .then(data => {
            const list = document.getElementById("conversationList");
            list.innerHTML = "";
            data.forEach(conv => {
                const isActive = conv.id === activeConversationId ? "active bg-primary bg-opacity-25" : "";
                const activeClass = conv.id === activeConversationId ? "active bg-primary bg-opacity-25" : "";
                // Use a default avatar if none provided
                const avatar = conv.avatar || "/img/default_avatar.png";
                const lastMsg = conv.lastMessage || "Chưa có tin nhắn";
                const isGroupMark = conv.isGroup ? `<span class="badge bg-secondary ms-1" style="font-size: 0.6em;">Group/Channel</span>` : "";

                const html = `
                    <div class="conversation-item d-flex align-items-center p-3 cursor-pointer ${activeClass}" onclick="openChat('${conv.id}', '${conv.name}', '${avatar}', this)">
                        <img src="${avatar}" class="rounded-circle me-3" width="45" height="45" onclick="event.stopPropagation(); viewChatPartnerProfile('${conv.id}', ${conv.isGroup})">
                        <div class="flex-grow-1 overflow-hidden">
                            <div class="d-flex justify-content-between align-items-center">
                                <h6 class="m-0 text-white text-truncate">${conv.name} ${isGroupMark}</h6>
                            </div>
                            <small class="text-muted text-truncate d-block">${lastMsg}</small>
                        </div>
                    </div>
                `;
                list.insertAdjacentHTML('beforeend', html);
                
                // Join signalr group
                connection.invoke("JoinConversation", conv.id);
            });
        });
}

function loadUsers() {
    fetch('/api/chatapi/users')
        .then(res => res.json())
        .then(data => {
            const privateList = document.getElementById("userList");
            const groupList = document.getElementById("groupUserList");
            privateList.innerHTML = "";
            groupList.innerHTML = "";

            data.forEach(user => {
                const avatar = user.avatar || "/img/default_avatar.png";
                
                // For private
                const pItem = `
                    <button type="button" class="list-group-item list-group-item-action bg-transparent text-white border-secondary d-flex align-items-center" onclick="startPrivateChat('${user.id}')">
                        <img src="${avatar}" class="rounded-circle me-2" width="30" height="30">
                        ${user.displayName} (@${user.username})
                    </button>
                `;
                privateList.insertAdjacentHTML('beforeend', pItem);

                // For group
                const gItem = `
                    <div class="form-check mb-2">
                        <input class="form-check-input" type="checkbox" value="${user.id}" id="chk_${user.id}">
                        <label class="form-check-label d-flex align-items-center" for="chk_${user.id}">
                            <img src="${avatar}" class="rounded-circle mx-2" width="25" height="25">
                            ${user.displayName}
                        </label>
                    </div>
                `;
                groupList.insertAdjacentHTML('beforeend', gItem);
            });
        });
}

function openChat(id, name, avatar, element) {
    activeConversationId = id;
    document.getElementById("activeConversationId").value = id;
    
    // UI Update
    document.querySelector(".chat-empty-state").classList.add("d-none");
    document.querySelector(".chat-active-state").classList.remove("d-none");
    document.querySelector(".chat-active-state").classList.add("d-flex");

    // Mobile: Show chat area
    document.getElementById("chatArea").classList.add("active");
    
    document.getElementById("currentChatName").innerText = name;
    document.getElementById("currentChatAvatar").src = avatar;

    // Load active selection style
    document.querySelectorAll(".conversation-item").forEach(el => el.classList.remove("active", "bg-primary", "bg-opacity-25"));
    if (element) {
        element.classList.add("active", "bg-primary", "bg-opacity-25");
    }

    // Load Messages
    fetch(`/api/chatapi/conversation/${id}/messages`)
        .then(res => res.json())
        .then(messages => {
            const msgList = document.getElementById("messagesList");
            msgList.innerHTML = "";
            messages.forEach(msg => appendMessage(msg));
            scrollToBottom();
        });

    // Check if it's a group, then show/hide Group Settings button
    fetch(`/api/chatapi/conversation/${id}/details`)
        .then(res => res.json())
        .then(data => {
            currentConversationDetails = data;
            const settingsBtn = document.getElementById("groupSettingsBtn");
            const messageInput = document.getElementById("messageInput");
            const muteIcon = document.getElementById("muteIcon");
            
            // Cập nhật trạng thái chuông (Tắt/Bật thông báo)
            if (data.isMuted) {
                muteIcon.className = "bi bi-bell-slash text-danger";
            } else {
                muteIcon.className = "bi bi-bell";
            }

            if (data.isGroup) {
                settingsBtn.classList.remove("d-none");
                
                // ReadOnly check logic
                if (data.isReadOnlyMode && data.myRole === "Member") {
                    messageInput.disabled = true;
                    messageInput.placeholder = "Chỉ Quản trị viên mới được nhắn vào nhóm này.";
                } else {
                    messageInput.disabled = false;
                    messageInput.placeholder = "Nhập tin nhắn...";
                }
            } else {
                settingsBtn.classList.add("d-none");
                messageInput.disabled = false;
                messageInput.placeholder = "Nhập tin nhắn...";
            }
        });
}

function appendMessage(msg) {
    const msgList = document.getElementById("messagesList");
    // Verify current user (needs a variable passed from Razor or check senderId)
    // Note: Assuming `currentUserId` is defined in Razor script section
    const isMe = msg.senderId === currentUserId;
    
    const wrapperClass = isMe ? "justify-content-end" : "justify-content-start";
    const bubbleClass = isMe ? "bg-primary text-white" : "bg-secondary bg-opacity-25 text-white";
    
    let contentHtml = '';
    if (msg.messageType === 'image') {
        contentHtml = `<img src="${msg.content}" style="max-width: 250px; border-radius: 8px;">`;
    } else if (msg.messageType === 'audio') {
        contentHtml = `<audio controls src="${msg.content}" style="max-width: 250px; height: 40px;"></audio>`;
    } else if (msg.messageType === 'file') {
        contentHtml = `<a href="${msg.content}" target="_blank" class="text-white text-decoration-underline"><i class="bi bi-file-earmark"></i> Tải tập tin đính kèm</a>`;
    } else if (msg.messageType === 'call_log') {
        contentHtml = `<div class="call-log-msg"><i class="bi bi-telephone-outbound me-2"></i>${msg.content}</div>`;
    } else {
        contentHtml = `<div>${msg.content}</div>`;
    }

    const html = `
        <div class="d-flex ${wrapperClass}">
            <div class="message-bubble ${bubbleClass} p-2 rounded-3" style="max-width: 75%;">
                ${!isMe ? `<small class="d-block text-info mb-1" style="font-size: 0.75rem;">${msg.senderName}</small>` : ''}
                ${contentHtml}
            </div>
        </div>
    `;
    msgList.insertAdjacentHTML('beforeend', html);
    scrollToBottom();
}

function scrollToBottom() {
    const list = document.getElementById("messagesList");
    if (list) {
        // Sử dụng setTimeout để đảm bảo trình duyệt đã render xong nội dung mới
        setTimeout(() => {
            list.scrollTo({
                top: list.scrollHeight,
                behavior: 'smooth' // Cuộn mượt mà
            });
        }, 50);
    }
}

// ========================== Search & Friends Logic ==========================
window.performSearch = function() {
    console.log("WebChat: Hàm performSearch đang được gọi...");
    const query = document.getElementById("searchInput").value.trim();
    if (!query) return;

    fetch(`/api/user/search?q=${encodeURIComponent(query)}`)
        .then(res => res.json())
        .then(data => {
            const list = document.getElementById("searchResults");
            list.innerHTML = "";
            if (data.length === 0) {
                list.innerHTML = '<p class="text-muted text-center mt-2">Không tìm thấy kết quả.</p>';
                return;
            }
            
            data.forEach(user => {
                let actionHtml = '';
                if (user.friendshipStatus === 'Accepted') {
                    actionHtml = `<span class="badge bg-success">Bạn bè</span>`;
                } else if (user.friendshipStatus === 'Pending') {
                    actionHtml = `<span class="badge bg-warning">Chờ XN</span>`;
                } else {
                    actionHtml = `<button class="btn btn-sm btn-outline-info" onclick="sendFriendRequest('${user.id}')">Kết bạn</button>`;
                }

                const html = `
                    <div class="list-group-item bg-transparent text-white border-secondary d-flex justify-content-between align-items-center">
                        <div class="d-flex align-items-center cursor-pointer" onclick="viewProfile('${user.id}')">
                            <img src="${user.avatar || '/img/default_avatar.png'}" class="rounded-circle me-2" width="40" height="40">
                            <div>
                                <h6 class="m-0">${user.displayName}</h6>
                                <small class="text-muted">@${user.username}</small>
                            </div>
                        </div>
                        <div>${actionHtml}</div>
                    </div>
                `;
                list.insertAdjacentHTML('beforeend', html);
            });
        })
        .catch(err => {
            console.error('Search error:', err);
            alert('Lỗi khi gọi API tìm kiếm: ' + err.message);
        });
}

window.viewProfile = function(userId) {
    fetch(`/api/user/profile/${userId}`)
        .then(res => res.json())
        .then(data => {
            const content = document.getElementById("profileContent");
            
            // Build extra info string
            let extraInfo = '';
            if (data.isSelf || data.friendshipStatus === 'Accepted') {
                extraInfo = `
                    <div class="mt-3 text-start">
                        <p class="mb-1"><i class="bi bi-envelope"></i> Email: ${data.email || 'N/A'}</p>
                        <p class="mb-1"><i class="bi bi-telephone"></i> SĐT: ${data.phone || 'N/A'}</p>
                    </div>
                `;
            } else {
                extraInfo = `<div class="mt-3 text-muted"><small><i class="bi bi-lock"></i> Thông tin cá nhân bị ẩn</small></div>`;
            }

            let actionBtn = '';
            if (!data.isSelf) {
                if (data.friendshipStatus === 'Accepted') {
                    actionBtn = `
                        <button class="btn btn-primary w-100 mt-3" onclick="startPrivateChat('${data.id}')">Nhắn tin</button>
                        <button class="btn btn-outline-danger w-100 mt-2" onclick="unfriend('${data.id}')">Hủy kết bạn</button>
                    `;
                } else if (data.friendshipStatus === 'Pending') {
                    // Logic here is simple, just show pending
                    actionBtn = `<button class="btn btn-secondary w-100 mt-3" disabled>Đang chờ xác nhận</button>`;
                } else {
                    actionBtn = `<button class="btn btn-info w-100 mt-3" onclick="sendFriendRequest('${data.id}')">Gửi lời mời kết bạn</button>`;
                }
            }

            content.innerHTML = `
                <img src="${data.avatar || '/img/default_avatar.png'}" class="rounded-circle mb-3" width="100" height="100">
                <h4>${data.displayName}</h4>
                <p class="text-muted">@${data.username} | Tuổi: ${data.age || 'N/A'}</p>
                ${extraInfo}
                ${actionBtn}
            `;

            const searchModal = bootstrap.Modal.getInstance(document.getElementById('searchUserModal'));
            if (searchModal) searchModal.hide();
            
            const profileModal = new bootstrap.Modal(document.getElementById('userProfileModal'));
            profileModal.show();
        });
}

window.sendFriendRequest = function(userId) {
    fetch(`/api/user/friend-request/${userId}`, { method: 'POST' })
        .then(res => res.json())
        .then(() => {
            alert('Đã gửi yêu cầu kết bạn!');
            // Reload search UI if active
            const searchInput = document.getElementById("searchInput").value;
            if (searchInput) window.performSearch();
        })
        .catch(err => alert('Lỗi gửi yêu cầu kết bạn'));
}

window.loadFriendRequests = function() {
    document.getElementById("friendRequestBadge").classList.add("d-none"); // Hide badge when opened
    fetch('/api/user/friend-requests')
        .then(res => res.json())
        .then(data => {
            const list = document.getElementById("friendRequestsList");
            list.innerHTML = "";
            if (data.length === 0) {
                list.innerHTML = '<p class="text-muted text-center mt-3 p-3">Không có lời mời nào mới.</p>';
                return;
            }
            data.forEach(user => {
                const html = `
                    <div class="list-group-item bg-transparent text-white border-secondary d-flex justify-content-between align-items-center p-3">
                        <div class="d-flex align-items-center">
                            <img src="${user.avatar || '/img/default_avatar.png'}" class="rounded-circle me-2" width="40" height="40">
                            <div>
                                <h6 class="m-0">${user.displayName}</h6>
                                <small class="text-muted">@${user.username}</small>
                            </div>
                        </div>
                        <div class="d-flex gap-1">
                            <button class="btn btn-sm btn-success" onclick="acceptFriendRequest('${user.id}')">Đồng ý</button>
                            <button class="btn btn-sm btn-outline-danger" onclick="declineFriendRequest('${user.id}')">Từ chối</button>
                        </div>
                    </div>
                `;
                list.insertAdjacentHTML('beforeend', html);
            });
        });
}

window.acceptFriendRequest = function(requesterId) {
    fetch(`/api/user/friend-accept/${requesterId}`, { method: 'POST' })
        .then(res => res.json())
        .then(data => {
            alert('Đã đồng ý kết bạn!');
            loadFriendRequests();
            loadConversations(); // Update list to show new friend in private chat list if needed
        })
        .catch(err => alert('Lỗi khi chấp nhận kết bạn'));
}

window.declineFriendRequest = function(requesterId) {
    if(!confirm("Bạn có chắc muốn từ chối?")) return;
    fetch(`/api/user/friend-decline/${requesterId}`, { method: 'POST' })
        .then(res => res.json())
        .then(data => {
            loadFriendRequests();
        })
        .catch(err => alert('Lỗi khi từ chối kết bạn'));
}

window.unfriend = function(friendId) {
    if(!confirm("Bạn có chắc muốn hủy kết bạn? Việc này sẽ xóa mối quan hệ bạn bè giữa hai người.")) return;
    fetch(`/api/user/friend-remove/${friendId}`, { method: 'POST' })
        .then(res => res.json())
        .then(data => {
            alert('Đã hủy kết bạn thành công.');
            const profileModal = bootstrap.Modal.getInstance(document.getElementById('userProfileModal'));
            if (profileModal) profileModal.hide();
            
            // Clear current chat if we just unfriended the active partner
            if (currentConversationDetails && !currentConversationDetails.isGroup) {
                const partner = currentConversationDetails.participants.find(p => p.id !== currentUserId);
                if (partner && partner.id === friendId) {
                    location.reload(); // Refresh to clear state
                }
            }
            
            loadConversations();
        })
        .catch(err => alert('Lỗi khi hủy kết bạn'));
}

window.viewChatPartnerProfile = function() {
    if (!currentConversationDetails || currentConversationDetails.isGroup) return;
    const partner = currentConversationDetails.participants.find(p => p.id !== currentUserId);
    if (partner) viewProfile(partner.id);
}

window.viewProfileByConversation = function(convId, isGroup) {
    if (isGroup === 'true') return; // Not handling group profile yet
    fetch(`/api/chatapi/conversations/${convId}`)
        .then(res => res.json())
        .then(data => {
            const partner = data.participants.find(p => p.id !== currentUserId);
            if (partner) viewProfile(partner.id);
        });
}

// ============================================================================

// Send Message
document.getElementById("sendMessageForm").addEventListener("submit", function (e) {
    e.preventDefault();
    const input = document.getElementById("messageInput");
    const content = input.value.trim();
    if (!content || !activeConversationId) return;

    connection.invoke("SendMessage", activeConversationId, content, "text")
        .then(() => input.value = "")
        .catch(err => alert("Không thể gửi tin nhắn. Hãy kiểm tra nếu kênh này chỉ Admin được gửi."));
});

// Create Private Chat
window.startPrivateChat = function(otherUserId) {
    fetch(`/api/chatapi/conversation/private/${otherUserId}`, { method: 'POST' })
        .then(res => res.json())
        .then(data => {
            const modal = bootstrap.Modal.getInstance(document.getElementById('newChatModal'));
            modal.hide();
            loadConversations();
            // Automatically open chat UI? Wait for loadConversations is easier, but ideally open it
        });
}

// Create Group Chat
document.getElementById("createGroupForm").addEventListener("submit", function(e) {
    e.preventDefault();
    const name = document.getElementById("groupName").value;
    const isChannel = document.getElementById("isChannel").checked;
    
    const checkedBoxes = document.querySelectorAll("#groupUserList input[type=checkbox]:checked");
    const participantIds = Array.from(checkedBoxes).map(cb => cb.value);

    fetch(`/api/chatapi/conversation/group`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, participantIds, isChannel })
    })
    .then(res => res.json())
    .then(data => {
        const modal = bootstrap.Modal.getInstance(document.getElementById('newChatModal'));
        modal.hide();
        loadConversations();
    });
});

// ========================== Group Management Logic ==========================
window.openGroupSettings = function() {
    if (!currentConversationDetails) return;
    
    const { id, isGroup, myRole, isReadOnlyMode, participants } = currentConversationDetails;
    if (!isGroup) return;

    // Show/hide Admin Controls
    const adminControls = document.getElementById("groupAdminControls");
    const inviteSection = document.getElementById("inviteFriendsSection");
    const readonlySwitch = document.getElementById("readonlySwitch");
    
    if (myRole === "Owner" || myRole === "Admin") {
        adminControls.classList.remove("d-none");
        inviteSection.classList.remove("d-none");
        readonlySwitch.checked = isReadOnlyMode;
        loadInviteList(participants);
    } else {
        adminControls.classList.add("d-none");
        inviteSection.classList.add("d-none");
    }

    // Render Members
    const membersList = document.getElementById("groupMembersList");
    membersList.innerHTML = "";

    participants.forEach(p => {
        let badge = '';
        if (p.role === "Owner") badge = '<span class="badge bg-danger">Chủ nhóm</span>';
        else if (p.role === "Admin") badge = '<span class="badge bg-warning">Quản trị</span>';
        
        let actions = '';
        if (myRole === "Owner" && p.id !== currentUserId) {
            if (p.role === "Admin") {
                actions = `<button class="btn btn-sm btn-outline-secondary" onclick="changeRole('${p.id}', 'Member')">Giáng cấp</button>`;
            } else if (p.role === "Member") {
                actions = `<button class="btn btn-sm btn-outline-warning" onclick="changeRole('${p.id}', 'Admin')">Cấp Admin</button>`;
            }
        }

        const html = `
            <div class="list-group-item bg-transparent text-white border-secondary d-flex justify-content-between align-items-center">
                <div class="d-flex align-items-center">
                    <img src="${p.avatar || '/img/default_avatar.png'}" class="rounded-circle me-2" width="30" height="30">
                    <div>
                        <span class="m-0">${p.displayName}</span> ${badge}
                    </div>
                </div>
                <div>${actions}</div>
            </div>
        `;
        membersList.insertAdjacentHTML('beforeend', html);
    });

    const modal = new bootstrap.Modal(document.getElementById('groupSettingsModal'));
    modal.show();
}

window.loadInviteList = function(existingParticipants) {
    fetch('/api/chatapi/users')
        .then(res => res.json())
        .then(friends => {
            const inviteList = document.getElementById("inviteUserList");
            inviteList.innerHTML = "";
            
            // Lọc ra những người chưa có trong nhóm
            const existingIds = existingParticipants.map(p => p.id);
            const nonMembers = friends.filter(f => !existingIds.includes(f.id));

            if (nonMembers.length === 0) {
                inviteList.innerHTML = '<p class="text-muted text-center small">Tất cả bạn bè đã có trong nhóm.</p>';
                return;
            }

            nonMembers.forEach(user => {
                const html = `
                    <div class="form-check mb-2">
                        <input class="form-check-input" type="checkbox" value="${user.id}" id="invite_${user.id}">
                        <label class="form-check-label d-flex align-items-center" for="invite_${user.id}">
                            <img src="${user.avatar || '/img/default_avatar.png'}" class="rounded-circle mx-2" width="25" height="25">
                            <small>${user.displayName}</small>
                        </label>
                    </div>
                `;
                inviteList.insertAdjacentHTML('beforeend', html);
            });
        });
}

window.inviteToGroup = function() {
    if (!activeConversationId) return;
    
    const checkedBoxes = document.querySelectorAll("#inviteUserList input[type=checkbox]:checked");
    const participantIds = Array.from(checkedBoxes).map(cb => cb.value);

    if (participantIds.length === 0) {
        alert("Vui lòng chọn ít nhất một người bạn để mời.");
        return;
    }

    fetch(`/api/chatapi/conversation/${activeConversationId}/participants`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(participantIds)
    })
    .then(res => res.json())
    .then(data => {
        alert("Đã mời thành viên thành công!");
        // Refresh settings to show new members
        fetch(`/api/chatapi/conversation/${activeConversationId}/details`)
            .then(res => res.json())
            .then(data => {
                currentConversationDetails = data;
                openGroupSettings();
            });
    })
    .catch(err => alert("Lỗi khi mời thành viên."));
}

window.toggleReadOnly = function(isReadOnly) {
    if (!activeConversationId) return;
    fetch(`/api/chatapi/conversation/${activeConversationId}/readonly`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(isReadOnly)
    }).then(res => res.json())
      .then(() => {
          // Re-fetch details to sync UI
          openChat(activeConversationId, currentConversationDetails.name, currentConversationDetails.avatar);
      });
}

window.changeRole = function(targetUserId, newRole) {
    if (!activeConversationId) return;
    fetch(`/api/chatapi/conversation/${activeConversationId}/role/${targetUserId}?newRole=${newRole}`, {
        method: 'PUT'
    }).then(res => res.json())
      .then(() => {
          // Re-open settings to refresh members list
          fetch(`/api/chatapi/conversation/${activeConversationId}/details`)
            .then(res => res.json())
            .then(data => {
                currentConversationDetails = data;
                openGroupSettings();
            });
      });
}

// Toggle Mute Notifications
window.toggleMute = function() {
    if (!activeConversationId || !currentConversationDetails) return;
    
    // Đảo ngược trạng thái hiện tại
    const isCurrentlyMuted = currentConversationDetails.isMuted;
    const newMuteState = !isCurrentlyMuted;

    fetch(`/api/chatapi/conversation/${activeConversationId}/mute`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(newMuteState)
    }).then(res => res.json())
      .then(data => {
          currentConversationDetails.isMuted = newMuteState;
          const muteIcon = document.getElementById("muteIcon");
          if (newMuteState) {
              muteIcon.className = "bi bi-bell-slash text-danger";
          } else {
              muteIcon.className = "bi bi-bell";
          }
      });
}

// ========================== File & Media Logic ==========================
window.uploadFile = function(input) {
    if (!input.files || input.files.length === 0) return;
    if (!activeConversationId) return;

    const file = input.files[0];
    const formData = new FormData();
    formData.append("file", file);

    fetch('/api/chatapi/upload', {
        method: 'POST',
        body: formData
    })
    .then(res => res.json())
    .then(data => {
        // Send message via SignalR with the uploaded file URL
        connection.invoke("SendMessage", activeConversationId, data.url, data.type)
            .catch(err => console.error(err));
        input.value = ""; // Reset input
    })
    .catch(err => alert('Lỗi tải file lên!'));
}

let mediaRecorder;
let audioChunks = [];

window.startRecording = function() {
    if (!activeConversationId) return;
    navigator.mediaDevices.getUserMedia({ audio: true })
        .then(stream => {
            mediaRecorder = new MediaRecorder(stream);
            mediaRecorder.start();

            const btn = document.getElementById("recordAudioBtn");
            btn.classList.replace("btn-danger", "btn-warning");
            btn.innerHTML = `<i class="bi bi-record-circle"></i>`;

            mediaRecorder.addEventListener("dataavailable", event => {
                audioChunks.push(event.data);
            });

            mediaRecorder.addEventListener("stop", () => {
                const audioBlob = new Blob(audioChunks, { type: 'audio/webm' });
                audioChunks = [];
                
                // Return original button state
                btn.classList.replace("btn-warning", "btn-danger");
                btn.innerHTML = `<i class="bi bi-mic"></i>`;

                // Upload
                const formData = new FormData();
                formData.append("file", audioBlob, "voice_message.webm");

                fetch('/api/chatapi/upload', {
                    method: 'POST',
                    body: formData
                })
                .then(res => res.json())
                .then(data => {
                    connection.invoke("SendMessage", activeConversationId, data.url, 'audio').catch(console.error);
                })
                .catch(err => alert('Lỗi gửi ghi âm'));
            });
        })
        .catch(err => alert('Không thể truy cập Microphone'));
}

window.stopRecording = function() {
    if (mediaRecorder && mediaRecorder.state !== "inactive") {
        mediaRecorder.stop();
        // Stop stream tracks
        mediaRecorder.stream.getTracks().forEach(track => track.stop());
    }
}

// ========================== WebRTC Logic ==========================
// ========================== ZEGOCLOUD Logic ==========================
const appID = 1147548185;
const serverSecret = "2b9dd39405451e24c152ceced266c7c1";

let zp = null;
let activeCallTargetId = null;
let currentCallOffer = null; 
let isCallConnected = false;
let isCallEnded = false;
let currentCallType = "thoại";
let callStartTime = null;
let callTimerInterval = null;

async function joinZegoRoom(roomID, isVideo) {
    if (zp) return; 
    
    const outerContainer = document.getElementById("zego-full-screen-container");
    const innerContainer = document.getElementById("zego-inner-container");
    outerContainer.style.display = "flex";

    try {
        if (typeof ZegoUIKitPrebuilt === 'undefined') {
            throw new Error("Thư viện gọi điện chưa được tải xong. Vui lòng thử lại sau vài giây.");
        }

        const kitToken = ZegoUIKitPrebuilt.generateKitTokenForTest(
            appID, 
            serverSecret, 
            roomID, 
            currentUserId || "user_" + Math.floor(Math.random()*1000), 
            currentUserName || "Người dùng"
        );
        
        zp = ZegoUIKitPrebuilt.create(kitToken);
        
        zp.joinRoom({
            container: innerContainer,
            scenario: {
                mode: ZegoUIKitPrebuilt.OneONoneCall,
            },
            showPreJoinView: false,
            turnOnMicrophoneWhenJoining: true,
            turnOnCameraWhenJoining: isVideo,
            showScreenSharingButton: false,
            showMyCameraToggleButton: true,
            showMyMicrophoneToggleButton: true,
            showAudioVideoSettingsButton: true,
            showLeaveRoomConfirmDialog: false,
            onLeaveRoom: () => {
                outerContainer.style.display = "none";
                endCall();
            }
        });
        
        // Ẩn modal gọi điện cũ
        const callModalElement = document.getElementById('callModal');
        const callModal = bootstrap.Modal.getInstance(callModalElement);
        if (callModal) callModal.hide();

    } catch (error) {
        console.error("WebChat ZEGO Error:", error);
        alert("Lỗi khởi tạo cuộc gọi: " + error.message);
        outerContainer.style.display = "none";
        isCallEnded = true;
        if (zp) {
            zp.destroy();
            zp = null;
        }
    }
}

// Start a call
window.startCall = function(isVideo) {
    if (!activeConversationId || currentConversationDetails.isGroup) {
        alert("Cuộc gọi chỉ hỗ trợ chat cá nhân 2 người.");
        return;
    }
    isCallEnded = false;

    const targetUserId = currentConversationDetails.participants.find(p => p.id !== currentUserId)?.id;
    if (!targetUserId) return;

    activeCallTargetId = targetUserId;
    currentCallType = isVideo ? "video" : "thoại";
    
    document.getElementById("callModalTitle").innerText = "Đang gọi...";
    document.getElementById("callStatusText").innerText = "Đang chờ đối phương trả lời...";
    document.getElementById("zego-call-container").classList.add("d-none");
    document.getElementById("old-call-ui").classList.remove("d-none");
    document.getElementById("acceptCallBtn").classList.add("d-none");

    const modal = new bootstrap.Modal(document.getElementById('callModal'));
    modal.show();

    // Chỉ gửi tín hiệu qua SignalR để báo hiệu
    connection.invoke("CallUser", activeCallTargetId, activeConversationId, { type: 'zego', isVideo: isVideo });
}

// Receive Call incoming
connection.on("ReceiveCall", function (callerId, callerName, conversationId, offer) {
    activeCallTargetId = callerId;
    currentCallOffer = offer;
    activeConversationId = conversationId; 
    isCallEnded = false; 

    document.getElementById("callModalTitle").innerText = "Cuộc gọi đến";
    document.getElementById("callStatusText").innerText = `${callerName} đang gọi cho bạn...`;
    document.getElementById("zego-call-container").classList.add("d-none");
    document.getElementById("old-call-ui").classList.remove("d-none");
    
    document.getElementById("acceptCallBtn").classList.remove("d-none");
    const modal = new bootstrap.Modal(document.getElementById('callModal'));
    modal.show();
});

window.acceptCall = function() {
    document.getElementById("acceptCallBtn").classList.add("d-none");
    isCallConnected = true;
    const isVideo = currentCallOffer.isVideo;
    
    connection.invoke("AnswerCall", activeCallTargetId, { type: 'zego_accepted', isVideo: isVideo });
    joinZegoRoom(activeConversationId, isVideo);
    startCallTimer();
}

connection.on("CallAccepted", function (targetId, answer) {
    isCallConnected = true;
    joinZegoRoom(activeConversationId, answer.isVideo);
    startCallTimer();
});

connection.on("ReceiveICECandidate", function (candidate) {
    if (peerConnection && peerConnection.remoteDescription) {
        peerConnection.addIceCandidate(new RTCIceCandidate(candidate)).catch(console.error);
    } else {
        pendingIceCandidates.push(candidate);
    }
});

function processPendingIceCandidates() {
    if (peerConnection && peerConnection.remoteDescription) {
        pendingIceCandidates.forEach(candidate => {
            peerConnection.addIceCandidate(new RTCIceCandidate(candidate)).catch(console.error);
        });
        pendingIceCandidates = [];
    }
}

function startCallTimer() {
    callStartTime = new Date();
    document.getElementById("callTimer").classList.remove("d-none");
    document.getElementById("callTimer").innerText = "00:00";
    
    callTimerInterval = setInterval(() => {
        const now = new Date();
        const diff = Math.floor((now - callStartTime) / 1000);
        const mins = Math.floor(diff / 60).toString().padStart(2, '0');
        const secs = (diff % 60).toString().padStart(2, '0');
        document.getElementById("callTimer").innerText = `${mins}:${secs}`;
    }, 1000);
}

function stopCallTimer() {
    if (callTimerInterval) {
        clearInterval(callTimerInterval);
        callTimerInterval = null;
    }
    document.getElementById("callTimer").classList.add("d-none");
}

window.endCall = function(isInitiator = true) {
    if (isCallEnded) return;
    isCallEnded = true;

    const duration = document.getElementById("callTimer").innerText;
    const status = isCallConnected ? "Completed" : "Missed";

    // Ẩn Zego fullscreen container
    const zegoContainer = document.getElementById("zego-full-screen-container");
    if (zegoContainer) zegoContainer.style.display = "none";

    if (zp) {
        zp.destroy();
        zp = null;
    }
    
    if (isInitiator && activeConversationId && activeCallTargetId) {
        connection.invoke("SaveCallLog", activeConversationId, activeCallTargetId, duration, currentCallType, status).catch(console.error);
    }

    if (isInitiator && activeCallTargetId) {
        connection.invoke("RejectCall", activeCallTargetId).catch(console.error);
    }

    stopCallTimer();
    isCallConnected = false;
    activeCallTargetId = null;

    const callModal = bootstrap.Modal.getInstance(document.getElementById('callModal'));
    if (callModal) callModal.hide();
}

connection.on("CallRejected", function (userId) {
    console.log("WebChat: Cuộc gọi bị từ chối hoặc kết thúc bởi đối phương.");
    endCall(false); // Không phải người khởi tạo kết thúc tại đây, chỉ đóng giao diện
});

