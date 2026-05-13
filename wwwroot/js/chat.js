const connection = new signalR.HubConnectionBuilder()
    .withUrl("/chatHub")
    .build();

let activeConversationId = null;

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

function loadConversations() {
    fetch('/api/chatapi/conversations')
        .then(res => res.json())
        .then(data => {
            const list = document.getElementById("conversationList");
            list.innerHTML = "";
            data.forEach(conv => {
                const isActive = conv.id === activeConversationId ? "active bg-primary bg-opacity-25" : "";
                // Use a default avatar if none provided
                const avatar = conv.avatar || "/img/default_avatar.png";
                const lastMsg = conv.lastMessage || "Chưa có tin nhắn";
                const isGroupMark = conv.isGroup ? `<span class="badge bg-secondary ms-1" style="font-size: 0.6em;">Group/Channel</span>` : "";

                const html = `
                    <div class="d-flex align-items-center p-2 rounded cursor-pointer mb-1 conversation-item ${isActive}" onclick="openChat('${conv.id}', '${conv.name}', '${avatar}')">
                        <img src="${avatar}" class="rounded-circle me-2" width="45" height="45">
                        <div class="overflow-hidden w-100">
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

function openChat(id, name, avatar) {
    activeConversationId = id;
    document.getElementById("activeConversationId").value = id;
    
    // UI Update
    document.querySelector(".chat-empty-state").classList.add("d-none");
    document.querySelector(".chat-active-state").classList.remove("d-none");
    document.querySelector(".chat-active-state").classList.add("d-flex");
    
    document.getElementById("currentChatName").innerText = name;
    document.getElementById("currentChatAvatar").src = avatar;

    // Load active selection style
    document.querySelectorAll(".conversation-item").forEach(el => el.classList.remove("active", "bg-primary", "bg-opacity-25"));
    event.currentTarget.classList.add("active", "bg-primary", "bg-opacity-25");

    // Load Messages
    fetch(`/api/chatapi/conversation/${id}/messages`)
        .then(res => res.json())
        .then(messages => {
            const msgList = document.getElementById("messagesList");
            msgList.innerHTML = "";
            messages.forEach(msg => appendMessage(msg));
            scrollToBottom();
        });
}

function appendMessage(msg) {
    const msgList = document.getElementById("messagesList");
    // Verify current user (needs a variable passed from Razor or check senderId)
    // Note: Assuming `currentUserId` is defined in Razor script section
    const isMe = msg.senderId === currentUserId;
    
    const wrapperClass = isMe ? "justify-content-end" : "justify-content-start";
    const bubbleClass = isMe ? "bg-primary text-white" : "bg-secondary bg-opacity-25 text-white";
    
    const html = `
        <div class="d-flex ${wrapperClass}">
            <div class="message-bubble ${bubbleClass} p-2 rounded-3" style="max-width: 75%;">
                ${!isMe ? `<small class="d-block text-info mb-1" style="font-size: 0.75rem;">${msg.senderName}</small>` : ''}
                <div>${msg.content}</div>
            </div>
        </div>
    `;
    msgList.insertAdjacentHTML('beforeend', html);
}

function scrollToBottom() {
    const list = document.getElementById("messagesList");
    list.scrollTop = list.scrollHeight;
}

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
