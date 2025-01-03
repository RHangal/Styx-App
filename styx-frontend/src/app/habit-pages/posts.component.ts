import { Component, OnInit } from '@angular/core';
import { PostsService } from './posts.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '@auth0/auth0-angular';
import { ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-posts',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './posts.component.html',
  styleUrl: './posts.component.sass',
})
export class PostsComponent implements OnInit {
  postType: string | null = null; // Dynamically set post type (e.g., "dogs", "smoking")
  posts: any[] = [];
  error: string | null = null;
  formData = {
    caption: '',
  };
  selectedMediaFile: File | null = null;
  auth0UserId: string | null = null;
  commentText: string = ''; // For top-level comment
  replyText: string = ''; // For reply text
  replyingToCommentId: string | null = null; // Track which comment is being replied to
  commentingOnPostId: string | null = null;
  isLoading: boolean = false; // Flag to track loading state

  constructor(
    private postsService: PostsService,
    public auth: AuthService,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    this.postType = this.route.snapshot.paramMap.get('postType');
    this.fetchPosts(this.postType ?? 'defaultType');
    this.auth.user$.subscribe((user) => {
      if (user && user.sub) {
        this.auth0UserId = user.sub; // Save auth0UserId for later use
      }
    });
  }

  fetchPosts(postType: string): void {
    this.postsService.getPostsByType(postType).subscribe(
      (data) => {
        if (data === null) {
          this.posts = [];
          this.error = 'No posts available for the selected type.';
        } else {
          this.posts = data;
          this.posts = this.posts.sort(
            (a, b) =>
              new Date(b.CreatedAt).getTime() - new Date(a.CreatedAt).getTime()
          );
          this.error = null;
        }
      },
      (err) => {
        console.error('Error fetching posts:', err);
        this.error = 'Failed to fetch posts. Please try again later.';
        this.posts = [];
      }
    );
  }

  // Handle file selection
  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.selectedMediaFile = input.files[0];
    } else {
      this.selectedMediaFile = null;
    }
  }

  // Handle form submission
  onSubmit(): void {
    // Set static values and retrieve dynamic ones
    if (!this.auth0UserId) {
      alert('User ID not found. Please log in.');
      return;
    }
    const postType = this.postType ?? 'defaultType';
    const name = localStorage.getItem('userName') || 'Anonymous'; // Fallback to 'Anonymous'
    const email = localStorage.getItem('userEmail') || '';

    this.isLoading = true;
    // Call the service to create the post
    this.postsService
      .createPostWithOptionalMedia(
        postType,
        name,
        email,
        this.auth0UserId,
        this.formData.caption,
        this.selectedMediaFile || undefined
      )
      .subscribe({
        next: (response) => {
          this.isLoading = false;
          console.log('Post created successfully:', response);

          // Check if the response contains the success message for coins
          if (
            response.message &&
            response.message.includes('500 coins rewarded successfully.')
          ) {
            // Show the alert that coins were rewarded
            alert(
              `Congratulations! ${response.message} Your total coins: ${response.totalCoins}`
            );
          } else {
            // Default post creation success alert
            alert('Post created!');
          }

          this.resetForm();
          this.fetchPosts(this.postType ?? 'defaultType');
        },
        error: (error) => {
          this.isLoading = false;
          console.error('Error creating post:', error);
          alert('Failed to create post.');
        },
      });
  }

  // Reset form after submission
  resetForm(): void {
    this.formData.caption = '';
    this.selectedMediaFile = null;
    const fileInput = document.getElementById('mediaFile') as HTMLInputElement;
    if (fileInput) {
      fileInput.value = ''; // Reset file input
    }
  }

  // Toggle comment form for a specific post
  toggleCommentForm(postId: string) {
    this.commentingOnPostId =
      this.commentingOnPostId === postId ? null : postId;
  }

  // Toggle reply form for a specific comment
  toggleReplyForm(commentId: string) {
    this.replyingToCommentId =
      this.replyingToCommentId === commentId ? null : commentId;
  }

  // Add a comment or reply
  addComment(postId: string, postEmail: string, commentId?: string) {
    const name = localStorage.getItem('userName');
    const email = localStorage.getItem('userEmail');

    if (!name || !email) {
      alert('User information is missing. Please log in.');
      return;
    }

    const text = commentId ? this.replyText : this.commentText;

    if (!text.trim()) {
      alert('Comment text is required.');
      return;
    }

    if (!this.auth0UserId) {
      alert('User ID not found. Please log in.');
      return;
    }

    this.postsService
      .addComment(
        postId,
        this.auth0UserId,
        text,
        email,
        postEmail,
        name,
        commentId || undefined
      )
      .subscribe({
        next: () => {
          alert(
            commentId
              ? 'Reply added successfully!'
              : 'Comment added successfully!'
          );
          this.resetCommentFields();
          this.fetchPosts(this.postType ?? 'defaultType');
        },
        error: (err) => {
          console.error('Error adding comment:', err);
          alert('Failed to add comment/reply.');
        },
      });
  }

  // Reset fields
  resetCommentFields() {
    this.commentText = '';
    this.replyText = '';
    this.commentingOnPostId = null;
    this.replyingToCommentId = null;
  }

  //toggle like
  onToggleLike(postId: string, commentId?: string, replyId?: string): void {
    if (!this.auth0UserId) {
      alert('User not authenticated. Please log in.');
      return;
    }

    this.postsService
      .toggleLike(postId, this.auth0UserId, commentId, replyId)
      .subscribe({
        next: (response) => {
          console.log('Like toggled successfully:', response);
          this.fetchPosts(this.postType ?? 'defaultType'); // Refresh the posts to reflect the updated like counts.
        },
        error: (err) => {
          console.error('Error toggling like:', err);
          alert('Failed to toggle like.');
        },
      });
  }

  //delete posts
  deletePost(postId: string): void {
    if (!this.auth0UserId) {
      console.error('User is not authenticated.');
      return;
    }

    const confirmDelete = window.confirm(
      'Are you sure you want to delete this post?'
    );
    if (!confirmDelete) {
      return; // Exit if the user cancels
    }

    this.postsService.deletePost(postId, this.auth0UserId).subscribe(
      () => {
        console.log('Post deleted successfully.');
        this.fetchPosts(this.postType ?? 'defaultType');
      },
      (error) => console.error('Error deleting post:', error)
    );
  }

  editComment(postId: string, commentId: string, replyId?: string): void {
    if (!this.auth0UserId) {
      console.error('User is not authenticated.');
      return;
    }

    // Find the post, comment, and reply (if provided)
    const post = this.posts.find((p) => p.id === postId);
    if (!post) {
      console.error('Post not found.');
      return;
    }

    const comment = post.Comments.find(
      (c: { CommentId: string }) => c.CommentId === commentId
    );
    if (!comment) {
      console.error('Comment not found.');
      return;
    }

    let currentText = comment.Text;
    if (replyId) {
      const reply = comment.Replies.find(
        (r: { CommentId: string | undefined }) => r.CommentId === replyId
      );
      if (!reply) {
        console.error('Reply not found.');
        return;
      }
      currentText = reply.Text;
    }

    // Prompt with the current text for editing
    const newText = window.prompt('Edit your comment:', currentText);
    if (!newText || newText === currentText) {
      console.log('Edit canceled or no changes made.');
      return;
    }

    // Prepare the request body

    replyId = replyId || undefined;

    this.postsService
      .editComment(newText, postId, this.auth0UserId, commentId, replyId)
      .subscribe(
        () => {
          console.log('Comment edited successfully.');
          // Update the comment/reply locally in the UI
          this.fetchPosts(this.postType ?? 'defaultType');
        },
        (error) => console.error('Error editing comment:', error)
      );
  }

  deleteComment(postId: string, commentId: string, replyId?: string): void {
    if (!this.auth0UserId) {
      console.error('User is not authenticated.');
      return;
    }
    this.postsService
      .deleteComment(postId, this.auth0UserId, commentId, replyId)
      .subscribe(
        () => {
          console.log('Comment deleted successfully.');
          // Update the comment/reply locally in the UI
          this.fetchPosts(this.postType ?? 'defaultType');
        },
        (error) => console.error('Error deleting comment:', error)
      );
  }

  confirmDeleteComment(postId: string, commentId: string): void {
    const confirmDelete = window.confirm(
      'Are you sure you want to delete this comment?'
    );
    if (confirmDelete) {
      this.deleteComment(postId, commentId);
    }
  }

  confirmDeleteReply(postId: string, commentId: string, replyId: string): void {
    const confirmDelete = window.confirm(
      'Are you sure you want to delete this reply?'
    );
    if (confirmDelete) {
      this.deleteComment(postId, commentId, replyId);
    }
  }

  isImage(url: string): boolean {
    // Check file extensions for images
    const imageExtensions = ['jpg', 'jpeg', 'png', 'gif', 'webp'];
    const fileExtension = url.split('.').pop()?.toLowerCase();
    return imageExtensions.includes(fileExtension || '');
  }
}
